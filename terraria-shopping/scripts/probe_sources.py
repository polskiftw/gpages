#!/usr/bin/env python3
import json
import urllib.parse
import urllib.request

API = "https://terraria.wiki.gg/api.php"
HEADERS = {"User-Agent": "polskiftw/gpages terraria-source-probe/1.0", "Accept": "application/json"}

def query(**params):
    params.setdefault("action", "cargoquery")
    params.setdefault("format", "json")
    params.setdefault("formatversion", "2")
    url = API + "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers=HEADERS)
    with urllib.request.urlopen(req, timeout=45) as response:
        return json.loads(response.read().decode("utf-8"))

for fields in [
    "name,hardmode,buy,vendor,plunder,fished",
    "name,hardmode,type,buy,vendor,plunder,fished",
]:
    print("ITEM FIELDS", fields)
    print(json.dumps(query(tables="Items", fields=fields, where='name="Music Box"', limit=10), ensure_ascii=False, indent=2))

for fields in ["name,item", "name,item,quantity,rate,mode,note", "name,item,isfromnpc"]:
    print("DROP FIELDS", fields)
    print(json.dumps(query(tables="Drops", fields=fields, where='item="Adhesive Bandage"', limit=20), ensure_ascii=False, indent=2))
