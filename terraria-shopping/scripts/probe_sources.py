#!/usr/bin/env python3
import json
import re
import urllib.parse
import urllib.request

API = "https://terraria.wiki.gg/api.php"
HEADERS = {"User-Agent": "polskiftw/gpages terraria-source-probe/1.4", "Accept": "application/json"}

def request(**params):
    params.setdefault("format", "json")
    params.setdefault("formatversion", "2")
    url = API + "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers=HEADERS)
    with urllib.request.urlopen(req, timeout=45) as response:
        return json.loads(response.read().decode("utf-8"))

pages = [
    "Music Box", "Armored Cavefish", "Active Stone Block", "Acorn",
    "Angler Earring", "Actuator", "Prismatic Lacewing", "Life Crystal",
]
payload = request(
    action="query", prop="revisions", titles="|".join(pages),
    rvprop="content", rvslots="main", redirects=1,
)
for page in payload.get("query", {}).get("pages", []):
    title = page.get("title")
    revs = page.get("revisions") or []
    text = (((revs[0] if revs else {}).get("slots") or {}).get("main") or {}).get("content", "")
    print("\n### PAGE", title)
    for line in text.splitlines():
        if re.search(r"\b(vendor|plunder|fished|drop|loot|bag loot|source|buy)\b", line, re.I):
            print(line[:1600])
