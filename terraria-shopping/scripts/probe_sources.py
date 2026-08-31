#!/usr/bin/env python3
import html
import json
import re
import urllib.parse
import urllib.request

API = "https://terraria.wiki.gg/api.php"
HEADERS = {"User-Agent": "polskiftw/gpages terraria-source-probe/1.5", "Accept": "application/json"}
ITEMS = [
    "Music Box", "Armored Cavefish", "Angler Earring", "Acorn", "Actuator",
    "Life Crystal", "Prismatic Lacewing", "Active Stone Block", "Adhesive Bandage", "Aglet",
]

def request(**params):
    params.setdefault("format", "json")
    params.setdefault("formatversion", "2")
    url = API + "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers=HEADERS)
    with urllib.request.urlopen(req, timeout=45) as response:
        return json.loads(response.read().decode("utf-8"))

def plain(value):
    value = re.sub(r"<style\b[^>]*>.*?</style>", " ", value, flags=re.I | re.S)
    value = re.sub(r"<script\b[^>]*>.*?</script>", " ", value, flags=re.I | re.S)
    value = re.sub(r"<br\s*/?>", " / ", value, flags=re.I)
    value = re.sub(r"<[^>]+>", " ", value)
    value = html.unescape(value)
    return re.sub(r"\s+", " ", value).strip()

for name in ITEMS:
    payload = request(
        action="parse",
        contentmodel="wikitext",
        prop="text",
        title=name,
        text=f'<div class="source-probe">{{{{itemsource|{name}|sep= / }}}}</div>',
    )
    rendered = ((payload.get("parse") or {}).get("text") or "")
    print("\n###", name)
    print("PLAIN:", plain(rendered)[:4000])
    print("HTML:", rendered[:6000].replace("\n", " "))
