# Downloads the Steam achievement icons (unlocked and locked) into Model/Icons, named after each
# achievement's API name: ACH_M_LVL5.jpg and ACH_M_LVL5_locked.jpg. The icons are the game's own art, so
# this repo doesn't include them; without them the Achievements tab shows plain coloured squares.
# Usage: python3 fetch_icons.py
import json
import os
import re
import sys
import urllib.request

APP_ID = 1105130
here = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(here, "Model", "Icons")


def get(url):
    request = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read()


# The community page embeds its data as JSON (React Query state), including both icon URLs.
page = get(f"https://steamcommunity.com/stats/{APP_ID}/achievements/?l=english").decode("utf-8")
match = re.search(r'"queryData":"((?:[^"\\]|\\.)*)"', page)
if not match:
    sys.exit("Couldn't find the achievement data on the Steam Community page.")
queries = json.loads(json.loads('"' + match.group(1) + '"'))["queries"]
steam = next(q["state"]["data"] for q in queries
             if isinstance(q["state"]["data"], dict) and "groups" in q["state"]["data"])
achievements = [a for group in steam["groups"] for a in group["achievements"]]

with open(os.path.join(here, "Model", "achievements.json")) as f:
    ours = {a["id"] for a in json.load(f)}
missing = ours - {a["api_name"] for a in achievements}
if missing:
    print("Not on Steam:", ", ".join(sorted(missing)))

os.makedirs(out, exist_ok=True)
count = 0
for a in achievements:
    if a["api_name"] not in ours:
        print("Skipping", a["api_name"], "(not in achievements.json)")
        continue
    # The two loadstone achievements only have a locked icon on Steam: use whichever one exists for both.
    unlocked = a.get("icon_achieved") or a.get("icon_unachieved")
    locked = a.get("icon_unachieved") or unlocked
    for url, suffix in ((unlocked, ""), (locked, "_locked")):
        if url:
            with open(os.path.join(out, a["api_name"] + suffix + ".jpg"), "wb") as f:
                f.write(get(url))
    count += 1
print(f"{count} achievements, icons in {out}")
