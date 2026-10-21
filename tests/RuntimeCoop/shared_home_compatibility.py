"""Real transport handshake rejection before host save overlay, including both UI dialogs."""
import json,time
from driver import command,state,wait_for
import multiplayer as g
from matrix import profile
from shared_home_acceptance import snapshots,emit,personal_files,OUT

g.ROLES=g.ALL

def compatibility(role):return command(role,'sharedHome',compatibilityState=True)

def main():
    profile('baseline',0,0,0)
    g.parallel('sharedHome',g.ALL,compatibilityCase='')
    command('host','sharedHome',enabled=True)
    command('host','host',transport='Direct',port=19050)
    for r in g.ALL[2:]:command(r,'join',transport='Direct',port=g.PORTS[r])
    emit('compatibility_two_existing_guests_accepted',wait_for(lambda:all(compatibility(r)['ready'] for r in g.ALL[2:]),30))
    original=personal_files('client')
    for case in ['game','coop','missing','extra','version','hash','unreadable','timeout']:
        command('client','sharedHome',compatibilityCase=case,compatibilityClear=True)
        command('host','sharedHome',compatibilityClear=True)
        command('client','join',transport='Direct',port=g.PORTS['client'])
        if case=='timeout':
            wait_for(lambda:state('host').get('peers')==3,10)
            command('host','sharedHome',compatibilityTimeout=True)
        emit('compatibility_'+case+'_disconnects_only_failed_guest',wait_for(lambda:bool(compatibility('client')['details']) and bool(compatibility('host')['details']) and
            state('client').get('peers')==0 and state('host').get('peers')==2,30))
        client=compatibility('client');host=compatibility('host')
        def correct_details(text):
            if not text:return False
            if case=='timeout':return '超时' in text or 'timed out' in text
            if case in ['game','coop']:return '-test' in text
            return ('ExtraValidationFixture' if case=='extra' else 'SharedValidationFixture') in text
        emit('compatibility_'+case+'_both_sides_receive_details',correct_details(client['details']) and correct_details(host['details']),client=client['details'],host=host['details'])
        emit('compatibility_'+case+'_before_any_save_takeover',not client['overlay'] and not client['active'] and personal_files('client')==original)
        emit('compatibility_'+case+'_other_guests_remain_ready',all(compatibility(r)['ready'] for r in g.ALL[2:]))
        if case=='hash':
            for r in ['host','client']:
                try:command(r,'sharedHome',uiScreenshot=True,screenshotName='compatibility-error.png')
                except RuntimeError as error:print('batch_screenshot_unavailable',r,str(error).splitlines()[0],flush=True)
        command('client','stop')
    command('client','sharedHome',compatibilityCase='',compatibilityClear=True)
    command('host','sharedHome',compatibilityClear=True)
    profile('rtt800_loss10',400,100,10)
    command('client','join',transport='Direct',port=g.PORTS['client'])
    emit('compatibility_matching_client_joins_under_latency_and_loss',wait_for(lambda:compatibility('client')['ready'] and command('client','sharedHome')['loadoutReady'] and g.peers_ready(),60))
    profile('baseline',0,0,0)
    for r in g.ALL[1:]:command(r,'stop')
    command('host','stop')
    command('host','sharedHome',enabled=False)
    command('client','sharedHome',compatibilityCase='hash')
    command('host','host',transport='Direct',port=19050)
    command('client','join',transport='Direct',port=g.PORTS['client'])
    emit('compatibility_shared_mode_off_preserves_normal_join',wait_for(lambda:state('host').get('peers')==1 and len(state('host').get('remotes',[]))==1,30))
    command('client','stop');command('host','stop')
    g.parallel('sharedHome',g.ALL,compatibilityNative=True,compatibilityClear=True)
    print('Compatibility suite complete',flush=True)

if __name__=='__main__':main()
