#!/usr/bin/env python3
import json
import urllib.parse
import urllib.request

API = "https://terraria.wiki.gg/api.php"
HEADERS = {"User-Agent": "polskiftw/gpages terraria-source-probe/1.2", "Accept": "application/json"}

def query(**params):
    params.setdefault("action", "cargoquery")
    params.setdefault("format", "json")
    params.setdefault("formatversion", "2")
    url = API + "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers=HEADERS)
    with urllib.request.urlopen(req, timeout=45) as response:
        return json.loads(response.read().decode("utf-8"))

def show(label, **params):
    print("\n###", label)
    try:
        print(json.dumps(query(**params), ensure_ascii=False, indent=2))
    except Exception as exc:
        print("EXCEPTION", repr(exc))

for item in ["Music Box", "Armored Cavefish", "Active Stone Block", "Aglet"]:
    for field in ["vendor__full", "plunder__full", "fished__full", "buy"]:
        show(f"ITEM {item} {field}", tables="Items", fields=f"name,{field}", where=f'name="{item}"', limit=10)

for table in ["NPCs", "NPCs_NPCIDs", "NPC"]:
    for field in ["name", "name,type", "name,environment", "name,boss"]:
        show(f"{table} {field}", tables=table, fields=field, where='name="Queen Bee"', limit=10)
