"""Validate Wiki response envelopes before a refresh request leaves its retry loop."""
from __future__ import annotations


def validate_wiki_response(payload: object, params: dict[str, object]) -> dict:
    action = params.get("action")
    if action == "cargoquery":
        context = f"Cargo {params.get('tables')} offset {params.get('offset', 0)}"
    elif action == "query":
        context = str(params.get("list") or action)
        if "cmcontinue" in params:
            context += " continuation"
    else:
        context = str(action)

    if not isinstance(payload, dict):
        raise RuntimeError(f"{context}: expected a JSON object")

    # MediaWiki can return either error format with HTTP 200, even if results
    # are also present. Never interpret one of these bodies as a final page.
    errors = payload.get("errors", [])
    if not isinstance(errors, list):
        raise RuntimeError(f"{context}: expected errors to be a list")
    if "error" in payload or errors:
        entries = [payload["error"]] if "error" in payload else errors
        codes = ", ".join(
            str(entry.get("code") or "unknown error") if isinstance(entry, dict) else "unknown error"
            for entry in entries
        )
        raise RuntimeError(f"{context}: MediaWiki API error ({codes})")

    if action == "cargoquery":
        rows = payload.get("cargoquery")
        if not isinstance(rows, list):
            raise RuntimeError(f"{context}: expected cargoquery to be a list")
        for i, row in enumerate(rows):
            if not isinstance(row, dict) or not isinstance(row.get("title"), dict):
                raise RuntimeError(f"{context}: expected cargoquery[{i}].title to be an object")
    elif action == "query" and params.get("list") == "categorymembers":
        query = payload.get("query")
        members = query.get("categorymembers") if isinstance(query, dict) else None
        if not isinstance(members, list):
            raise RuntimeError(f"{context}: expected query.categorymembers to be a list")
        for i, member in enumerate(members):
            if not isinstance(member, dict) or not isinstance(member.get("title"), str):
                raise RuntimeError(f"{context}: expected query.categorymembers[{i}].title to be a string")
        if "continue" in payload:
            continuation = payload["continue"]
            token = continuation.get("cmcontinue") if isinstance(continuation, dict) else None
            if not isinstance(token, str) or not token:
                raise RuntimeError(f"{context}: expected a nonempty continue.cmcontinue string")
    elif action == "parse":
        parsed = payload.get("parse")
        if not isinstance(parsed, dict) or not isinstance(parsed.get("text"), str):
            raise RuntimeError(f"{context}: expected parse.text to be a string")

    return payload
