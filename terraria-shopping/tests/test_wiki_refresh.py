"""Offline regression tests for HTTP 200 Wiki errors during refresh."""
import contextlib
import importlib
import io
import json
from pathlib import Path
import runpy
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch
from urllib.parse import parse_qs, urlsplit

SCRIPTS = Path(__file__).resolve().parents[1] / "scripts"
sys.path.insert(0, str(SCRIPTS))

LEGACY_ERROR = {"error": {"code": "maxlag", "info": "Try again later"}}
MODERN_ERROR = {"errors": [{"code": "ratelimited", "text": "Try again later"}]}


class Response(io.BytesIO):
    status = 200
    headers = {}

    def __init__(self, payload):
        super().__init__(json.dumps(payload).encode())


def request_params(request):
    return parse_qs(request.data.decode() if request.data else urlsplit(request.full_url).query)


def recipe_row(name):
    return {"title": {"result": name, "args": "Wood", "amount": "1", "station": "By hand"}}


def recipe_page(offset, limit):
    # Six valid pages meet the production count and sentinel checks even
    # when a seventh page is incorrectly discarded as end-of-table.
    sentinels = ["Shellphone", "Ankh Shield", "Terraspark Boots", "Zenith"]
    return [
        recipe_row(sentinels[i] if i < len(sentinels) else f"Fixture item {i}")
        for i in range(offset, offset + limit)
    ]


class WikiRefreshTests(unittest.TestCase):
    def run_recipe_entrypoint(self, final_responses, initial_errors=(), *, error=None):
        offsets = []
        first = iter(initial_errors)
        final = iter(final_responses)

        def urlopen(request, **_kwargs):
            params = request_params(request)
            self.assertEqual(params["tables"], ["Recipes"])
            offset, limit = int(params["offset"][0]), int(params["limit"][0])
            self.assertEqual(limit, 500)
            offsets.append(offset)
            if offset == 0:
                response = next(first, None)
                if response is not None:
                    return Response(response)
            if offset < 3000:
                return Response({"cargoquery": recipe_page(offset, limit)})
            self.assertEqual(offset, 3000)
            return Response(next(final))

        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            scripts = root / "scripts"
            scripts.mkdir()
            entry = scripts / "build_data.py"
            shutil.copyfile(SCRIPTS / entry.name, entry)
            output = root / "data.generated.js"
            original = b"previous successful catalog\n"
            output.write_bytes(original)
            with patch("urllib.request.urlopen", side_effect=urlopen), patch("time.sleep") as sleep, contextlib.redirect_stderr(io.StringIO()):
                if error:
                    with self.assertRaisesRegex(RuntimeError, error):
                        runpy.run_path(str(entry), run_name="__main__")
                    self.assertEqual(output.read_bytes(), original)
                    count = None
                else:
                    with self.assertRaises(SystemExit) as exit_info:
                        runpy.run_path(str(entry), run_name="__main__")
                    self.assertEqual(exit_info.exception.code, 0)
                    count = json.loads(output.read_text().removeprefix("window.TERRARIA_RECIPE_DATA=").rstrip(";\n"))["recipeCount"]
            sleeps = [args[0] for args, _kwargs in sleep.call_args_list]

        return offsets, sleeps, count

    def test_persistent_http_200_error_leaves_previous_catalog_intact(self):
        offsets, sleeps, _ = self.run_recipe_entrypoint(
            [LEGACY_ERROR] * 4, error=r"Cargo Recipes offset 3000: MediaWiki API error \(maxlag\)"
        )
        self.assertEqual(offsets, list(range(0, 3000, 500)) + [3000] * 4)
        self.assertEqual(sleeps[-3:], [1, 2, 4])

    def test_error_retries_same_offset_and_successful_tail_is_kept(self):
        offsets, sleeps, count = self.run_recipe_entrypoint(
            [MODERN_ERROR | {"cargoquery": []}, {"cargoquery": [recipe_row("Tail item")]}],
            initial_errors=[LEGACY_ERROR],
        )
        self.assertEqual(offsets, [0] + list(range(0, 3000, 500)) + [3000, 3000])
        self.assertEqual(sleeps, [1] + [0.15] * 6 + [1])
        self.assertEqual(count, 3001)

    def test_explicit_empty_cargo_page_ends_pagination_without_retry(self):
        offsets, sleeps, count = self.run_recipe_entrypoint([{"cargoquery": [], "warnings": {"cargoquery": "notice"}}])
        self.assertEqual(offsets, list(range(0, 3500, 500)))
        self.assertEqual(sleeps, [0.15] * 6)
        self.assertEqual(count, 3000)

    def test_malformed_cargo_pages_retry_in_all_request_clients(self):
        malformed = [None, [], {}, {"cargoquery": {}}, {"cargoquery": None}, {"cargoquery": [{}]}, {"cargoquery": [{"title": []}]}]
        params = {"action": "cargoquery", "tables": "Drops", "offset": 500}
        for name in ("build_data", "build_fishing", "build_availability"):
            module = importlib.import_module(name)
            for payload in malformed:
                with self.subTest(client=name, payload=payload), patch("urllib.request.urlopen", side_effect=[Response(payload), Response({"cargoquery": []})]) as open_url, patch("time.sleep") as sleep:
                    self.assertEqual(module.request_json(params), {"cargoquery": []})
                    self.assertEqual(open_url.call_count, 2)
                    self.assertEqual([request_params(c.args[0])["offset"] for c in open_url.call_args_list], [["500"], ["500"]])
                    sleep.assert_called_once()

    def test_bad_category_envelope_retries_same_continuation(self):
        fishing = importlib.import_module("build_fishing")
        start = {"query": {"categorymembers": [{"title": "First"}]}, "continue": {"cmcontinue": "page|next"}}
        malformed = {"query": {}}
        bad_token = {"query": {"categorymembers": []}, "continue": {}}
        end = {"query": {"categorymembers": [{"title": "Second"}]}}
        with patch("urllib.request.urlopen", side_effect=list(map(Response, (start, malformed, bad_token, end)))) as open_url, patch("time.sleep") as sleep:
            self.assertEqual(fishing.fetch_fished_items(), {"First", "Second"})
            tokens = [request_params(c.args[0]).get("cmcontinue") for c in open_url.call_args_list]
            self.assertEqual(tokens, [None] + [["page|next"]] * 3)
            self.assertEqual([c.args[0] for c in sleep.call_args_list], [0.8, 1.6])

    def test_parse_clients_reject_errors_and_missing_text_inside_retry(self):
        params = {"action": "parse", "prop": "text", "text": "fixture"}
        complete = {"parse": {"text": "<p>fixture</p>"}}
        for name in ("build_availability", "build_vendors", "repair_availability"):
            module = importlib.import_module(name)
            kwargs = {"post": True} if name == "build_availability" else {}
            with self.subTest(client=name), patch("urllib.request.urlopen", side_effect=list(map(Response, (MODERN_ERROR, {"parse": {}}, complete)))) as open_url, patch("time.sleep") as sleep:
                self.assertEqual(module.request_json(params, **kwargs), complete)
                self.assertEqual([c.args[0].get_method() for c in open_url.call_args_list], ["POST"] * 3)
                self.assertEqual([request_params(c.args[0]) for c in open_url.call_args_list], [{"action": ["parse"], "prop": ["text"], "text": ["fixture"]}] * 3)
                self.assertEqual([c.args[0] for c in sleep.call_args_list], [1, 2])


if __name__ == "__main__":
    unittest.main()
