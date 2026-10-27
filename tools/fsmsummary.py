"""fsmsummary.py <graph.json>... : one line per state (index, name, action types) + transitions with condition types."""
import json, sys

def short(t):
    return (t or "").split(",")[0].split(".")[-1]

for path in sys.argv[1:]:
    g = json.load(open(path, encoding="utf-8"))
    nodes = g["nodes"]
    idx = {n.get("$id"): i for i, n in enumerate(nodes)}
    print("####", path.split("/")[-1].split("\\")[-1])
    for i, n in enumerate(nodes):
        acts = (n.get("_actionList") or {}).get("actions", [])
        names = [short(a.get("$type")) + ("(off)" if a.get("_isDisabled") else "") for a in acts]
        print(f"  [{i}] {n.get('_name') or short(n.get('$type'))}: {', '.join(names)}")
    for c in g.get("connections", []):
        s, t = idx.get(c["_sourceNode"]["$ref"]), idx.get(c["_targetNode"]["$ref"])
        cond = c.get("_condition") or {}
        ct = [short(x.get("$type")) for x in cond.get("conditions", [])] or [short(cond.get("$type"))]
        ev = json.dumps(cond)
        evn = ""
        if '"eventName"' in ev:
            try:
                evn = " ev=" + cond.get("eventName", {}).get("_value", "?")
            except AttributeError:
                pass
        print(f"    {s}->{t} {','.join(x for x in ct if x)}{evn}")
