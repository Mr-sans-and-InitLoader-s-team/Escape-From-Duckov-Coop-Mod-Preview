"""Native display contents, guest shop purchases and crafting stay host-owned."""
import json
import multiplayer as g
from driver import command,wait_for
from shared_home_acceptance import snapshots,emit,OUT,personal_files

g.ROLES=g.ALL
emit('warehouse_automatically_recovers_after_map_return',wait_for(lambda:all(v['storage']==snapshots()['host']['storage'] for v in snapshots().values()),20))
def displays():return g.parallel('sharedHome',displayCatalog=True)
display=displays()['host']
emit('native_display_contents_replicated',wait_for(lambda:all(v==display for v in displays().values()),20),displays=displays())
before=snapshots()
shops=command('host','sharedHome',shopCatalog=True)
shop=next(s for s in shops if s['MerchantID']=='Merchant_Normal')
entry=next(e for e in shop['entries'] if e['id']==13)
old=sum(i['count'] for i in before['host']['storage']['items'] if i['type']==entry['id'])
command('client','sharedHome',shopBuy=shop['MerchantID'],id=entry['id'])
def shop_done():
    values=snapshots()
    stock=sum(i['count'] for i in values['host']['storage']['items'] if i['type']==entry['id'])
    return stock==old+entry['amount'] and all(v['money']==before['host']['money']-entry['price'] and v['storage']==values['host']['storage'] for v in values.values())
emit('guest_shop_buy_host_pays_once_and_warehouse_gets_output',wait_for(shop_done,20),before=before['host']['money'],price=entry['price'],after={r:v['money'] for r,v in snapshots().items()})
craft=next(f for f in command('host','sharedHome',craftCatalog=True) if f['id']=='1001')
before=snapshots()
old=sum(i['count'] for i in before['host']['storage']['items'] if i['type']==craft['result'])
command('guest3','sharedHome',craft=craft['id'])
def crafted():
    values=snapshots()
    count=sum(i['count'] for i in values['host']['storage']['items'] if i['type']==craft['result'])
    return count==old+craft['amount'] and all(v['storage']==values['host']['storage'] for v in values.values())
emit('guest_native_crafting_output_reaches_common_warehouse_once',wait_for(crafted,20))
fixture=json.loads((OUT/'personal-fixture.json').read_text(encoding='utf-8-sig'))
for role in g.ALL[1:]:command(role,'stop')
emit('display_and_commerce_disconnect_preserve_personal_files',all(personal_files(r)==fixture['hashes'][r] for r in g.ALL[1:]))
print('Commerce and display suite complete',flush=True)
