#!/usr/bin/env python3
import json
import urllib.parse
import urllib.request

API = "https://terraria.wiki.gg/api.php"
HEADERS = {"User-Agent": "polskiftw/gpages terraria-source-probe/1.1", "Accept": "application/json"}

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

for field in ["buy", "vendor", "plunder", "fished", "type", "source", "sell", "value"]:
    show("ITEM " + field, tables="Items", fields=f"name,{field}", where='name="Music Box"', limit=10)

for field in ["boss", "type", "hardmode", "environment", "friendly"]:
    show("NPC " + field, tables="NPCs", fields=f"name,{field}", where='name="Queen Bee"', limit=10)

show("DROP adhesive", tables="Drops", fields="name,item,isfromnpc", where='item="Adhesive Bandage"', limit=20)
show("DROP music box", tables="Drops", fields="name,item,isfromnpc", where='item="Music Box"', limit=20)
show("DROP aglet", tables="Drops", fields="name,item,isfromnpc", where='item="Aglet"', limit=20)
