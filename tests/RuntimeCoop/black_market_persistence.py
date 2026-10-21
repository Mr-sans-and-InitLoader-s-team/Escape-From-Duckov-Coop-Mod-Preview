"""Native refresh button, local save/load and raid/base lifetime checks."""
import time
from black_market_acceptance import group,snapshot,market,emit,profile
from driver import command,wait_for

def main():
    profile('baseline',0,0,0)
    for role in group.ALL:
        command(role,'blackMarket',open=True,chance=1,elapsed=0)
        before=snapshot()
        command(role,'invoke',type='Duckov.BlackMarkets.UI.BlackMarketView',method='OnRefreshBtnClicked')
        time.sleep(.5)
        after=snapshot()
        emit(role+'_native_refresh_button',market(after[role])['chance']==0 and
             all(market(after[r])['chance']==market(before[r])['chance'] for r in group.ALL if r!=role),before=before,after=after)
        own=market(after[role])
        command(role,'invoke',type='Duckov.BlackMarkets.UI.BlackMarketView',method='OnRefreshBtnClicked')
        blocked=command(role,'blackMarket')
        emit(role+'_zero_chance_blocks_refresh',market(blocked)['supplies']==own['supplies'] and
             market(blocked)['demands']==own['demands'] and market(blocked)['chance']==0,state=blocked)
        command(role,'blackMarket',elapsed=own['interval']*2+5)
        recovered=wait_for(lambda:market(command(role,'blackMarket'))['chance']==2,5)
        emit(role+'_two_elapsed_periods',recovered,state=command(role,'blackMarket'))
        command(role,'blackMarket',close=True)
    expected={}
    for index,role in enumerate(group.ALL):
        value=command(role,'blackMarket',chance=index,elapsed=30+index*10,call='Save')
        expected[role]=market(value)
        command(role,'blackMarket',chance=4,elapsed=0)
        loaded=command(role,'blackMarket',call='Load')
        emit(role+'_native_save_load',market(loaded)['chance']==index and
             market(loaded)['last']==expected[role]['last'],saved=expected[role],loaded=loaded)
    group.rejoin()
    after=snapshot()
    emit('rejoin_keeps_personal_markets',all(market(after[r])['chance']==expected[r]['chance'] and
         market(after[r])['last']==expected[r]['last'] for r in group.ALL),states=after)
    entered=group.maps.enter('Level_GroundZero_Main','client')
    emit('native_raid_entry',entered['loaded'],result=entered)
    returned=group.maps.leave('guest4')
    emit('native_return_to_base',returned['pass'],result=returned)
    after=snapshot()
    emit('base_return_keeps_personal_markets',all(market(after[r])['chance']==expected[r]['chance'] and
         market(after[r])['last']==expected[r]['last'] for r in group.ALL),states=after)
    group.parallel('blackMarket',open=True)
    before=snapshot();time.sleep(3);after=snapshot()
    emit('base_return_countdowns_tick',all(market(after[r])['remaining']<market(before[r])['remaining']-2 and
         after[r]['view']['eta']!=before[r]['view']['eta'] for r in group.ALL),before=before,after=after)
    group.parallel('blackMarket',close=True)

if __name__=='__main__':main()
