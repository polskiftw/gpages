import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import tag_gremlin as tg


FIXTURE = """
<!doctype html>
<html>
<body>
<div>332228 tags</div>
<a href="tags.php?page=2">101-200</a>
<a href="tags.php?page=3323">Last &gt;&gt;</a>

<table>
<tr><td>TagID</td><td>Tag</td><td>Uses</td><td colspan="2">Votes</td><td>Synonyms</td></tr>
<tr><td>178</td><td>alpha</td><td>482,121</td><td>+3,704,177</td><td>-2,334</td><td>2 [+]</td></tr>
<tr style="display:none"><td colspan="5">Synonyms: <a href="?q=a">alpha one</a>, <a href="?q=b">alpha.two</a></td></tr>
<tr><td>18</td><td>beta</td><td>359113</td><td>+2623224</td><td>-3511</td><td>0 [+]</td></tr>
<tr style="display:none"><td colspan="5">No synonyms.</td></tr>
</table>

<table>
<tr><td>TagID</td><td>Tag</td><td>Uses</td><td colspan="2">Votes</td><td>Synonyms</td></tr>
<tr><td>97</td><td>gamma</td><td>70,751</td><td>+539355</td><td>-3775</td><td>1 [+]</td></tr>
<tr style="display:none"><td colspan="5"><a href="?q=g">gamma alias</a></td></tr>
</table>
</body>
</html>
"""


PLAIN_TEXT_FIXTURE = """
<!doctype html>
<html><body>
<div>3 tags</div>
<a href="tags.php?page=1">Last</a>
<table>
<tr><td>TagID</td><td>Tag</td><td>Uses</td><td colspan="2">Votes</td><td>Synonyms</td></tr>
<tr><td>1</td><td>alpha</td><td>10</td><td>+3</td><td>-1</td><td>3 [+]</td></tr>
<tr style="display:none"><td colspan="5">first alias, second_alias, third.alias</td></tr>
<tr><td>2</td><td>beta</td><td>9</td><td>+2</td><td>-0</td><td></td></tr>
<tr style="display:none"><td colspan="5"></td></tr>
<tr><td>3</td><td>gamma</td><td>8</td><td>+1</td><td>-0</td><td>2 [+]</td></tr>
<tr style="display:none"><td colspan="5"><span>one</span><br><span>two</span></td></tr>
</table>
</body></html>
"""


class ParserTests(unittest.TestCase):
    def test_full_page_shape(self):
        parsed = tg.parse_tags_page(FIXTURE)
        self.assertEqual(parsed.reported_total, 332228)
        self.assertEqual(parsed.final_page, 3323)
        self.assertEqual(len(parsed.tags), 3)
        self.assertEqual(parsed.synonym_mismatches, 0)

        alpha = parsed.tags[0]
        self.assertEqual(alpha.tag_id, 178)
        self.assertEqual(alpha.name, "alpha")
        self.assertEqual(alpha.uses, 482121)
        self.assertEqual(alpha.upvotes, 3704177)
        self.assertEqual(alpha.downvotes, 2334)
        self.assertEqual(alpha.reported_synonym_count, 2)
        self.assertEqual(alpha.synonyms, ["alpha one", "alpha.two"])
        self.assertTrue(alpha.synonym_parse_ok)

        beta = parsed.tags[1]
        self.assertEqual(beta.synonyms, [])
        self.assertTrue(beta.synonym_parse_ok)

    def test_synonym_mismatch_is_not_silenced(self):
        html = FIXTURE.replace("<td>2 [+]</td>", "<td>3 [+]</td>", 1)
        parsed = tg.parse_tags_page(html)
        self.assertEqual(parsed.synonym_mismatches, 1)
        self.assertFalse(parsed.tags[0].synonym_parse_ok)

    def test_plain_text_synonyms_and_blank_zero_count(self):
        parsed = tg.parse_tags_page(PLAIN_TEXT_FIXTURE)
        self.assertEqual(len(parsed.tags), 3)
        self.assertEqual(parsed.synonym_mismatches, 0)
        self.assertEqual(
            parsed.tags[0].synonyms,
            ["first alias", "second_alias", "third.alias"],
        )
        self.assertEqual(parsed.tags[1].reported_synonym_count, 0)
        self.assertEqual(parsed.tags[1].synonyms, [])
        self.assertEqual(parsed.tags[2].synonyms, ["one", "two"])


class CliTests(unittest.TestCase):
    def test_numeric_worker_shorthand(self):
        self.assertEqual(
            tg._expand_numeric_worker_shorthand(
                ["harvest", "--url", "https://example.invalid/tags.php", "-8"]
            ),
            [
                "harvest",
                "--url",
                "https://example.invalid/tags.php",
                "--workers",
                "8",
            ],
        )


class DatabaseTests(unittest.TestCase):
    def test_complete_database_and_exports(self):
        parsed = tg.parse_tags_page(FIXTURE)
        parsed.reported_total = 3
        parsed.final_page = 1

        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            db_path = root / "test.sqlite3"
            db = tg.open_db(db_path)
            tg.set_meta(db, "reported_total", 3)
            tg.set_meta(db, "final_page", 1)
            tg.set_meta(db, "rows_per_page", 3)
            db.commit()

            tg.write_page(
                db,
                page=1,
                parsed=parsed,
                attempts=1,
                http_status=200,
                latency_ms=25,
                body_sha256="abc",
            )

            report = tg.verify_db(db)
            self.assertTrue(report["complete"])
            self.assertEqual(report["tag_count"], 3)
            self.assertEqual(
                db.execute("SELECT COUNT(*) FROM tag_synonyms").fetchone()[0],
                3,
            )
            db.close()

            args = type("Args", (), {
                "db": str(db_path),
                "out_dir": str(root / "out"),
                "allow_incomplete": False,
            })()
            self.assertEqual(tg.export_command(args), 0)

            self.assertEqual(
                (root / "out" / "tags.txt").read_text(encoding="utf-8"),
                "alpha\nbeta\ngamma\n",
            )
            synonyms = (root / "out" / "synonyms.tsv").read_text(encoding="utf-8")
            self.assertIn("178\talpha\talpha one", synonyms)
            self.assertIn("97\tgamma\tgamma alias", synonyms)


if __name__ == "__main__":
    unittest.main()
