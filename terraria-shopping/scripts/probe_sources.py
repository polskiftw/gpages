#!/usr/bin/env python3
import json
import urllib.parse
import urllib.request

API = "https://terraria.wiki.gg/api.php"
HEADERS = {"User-Agent": "polskiftw/gpages terraria-source-probe/1.3", "Accept": "application/json"}

def request(**params):
    params.setdefault("format", "json")
    params.setdefault("formatversion", "2")
    url = API + "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers=HEADERS)
    with urllib.request.urlopen(req, timeout=45) as response:
        return json.loads(response.read().decode("utf-8"))

def show(label, **params):
    print("\n###", label)
    try:
        print(json.dumps(request(**params), ensure_ascii=False, indent=2))
    except Exception as exc:
        print("EXCEPTION", repr(exc))

for table in ["Items", "Drops", "NPCs"]:
    show(f"FIELDS {table}", action="cargofields", table=table)

for item in ["Music Box", "Armored Cavefish", "Active Stone Block", "Aglet"]:
    for field in ["vendor__full", "plunder__full", "fished__full", "buy"]:
        show(
            f"ITEM {item} {field}", action="cargoquery", tables="Items",
            fields=f"name,{field}", where=f'name="{item}"', limit=10,
        )
