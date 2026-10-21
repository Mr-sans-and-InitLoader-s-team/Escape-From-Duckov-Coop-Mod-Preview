"""Retain per-file verdicts; preliminary failures are never silently replaced."""
import json
from driver import ROOT

def main():
    summaries={}
    for path in sorted(ROOT.glob('*.out.log')):
        categories={}
        for line in path.read_text(encoding='utf-8-sig',errors='replace').splitlines():
            if not line.startswith('{'):continue
            try:row=json.loads(line)
            except ValueError:continue
            category=row.get('category','uncategorized')
            bucket=categories.setdefault(category,{'rows':0,'pass':0,'fail':0,'ungraded':0})
            bucket['rows']+=1
            bucket['pass' if row.get('pass') is True else 'fail' if row.get('pass') is False else 'ungraded']+=1
        if categories:summaries[path.name]=categories
    target=ROOT/'result-file-summary.json'
    target.write_text(json.dumps(summaries,ensure_ascii=False,indent=2),encoding='utf-8')
    names=['final-damage.out.log','complete-snapshot-buffs.out.log','expiry.out.log','expiry-remaining.out.log',
        'four-player-remaining-journey.out.log','four-player-teardown-fixed-journey.out.log',
        'four-player-override-and-remaining.out.log','four-player-protected-rejoin-retry.out.log',
        'four-player-combat-resumed.out.log','four-player-weak-combat-retry.out.log']
    for name in names:print(name,json.dumps(summaries.get(name),ensure_ascii=False))

if __name__=='__main__':main()
