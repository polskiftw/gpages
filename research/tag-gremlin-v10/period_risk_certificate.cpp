#define TAG_GREMLIN_DYNAMIC_NO_MAIN
#include "period_dynamic_certificate.cpp"

#include <array>
#include <iomanip>

struct RiskPolicy {
    string name;
    array<int,5> len2;
    array<int,5> len3;
    bool margin_rank=false;
};

struct RiskLenStats {
    int probes=0, closed=0, sat=0, fresh=0, pruned=0;
};

struct RiskStats {
    DynStats all;
    RiskLenStats len2, len3;
};

static int riskThreshold(const RiskPolicy &pol,const string &q,int support){
    int s=max(0,min(4,support));
    if(q.size()==2) return pol.len2[s];
    if(q.size()==3) return pol.len3[s];
    return 1000000000;
}

static string chooseRiskCert(Sim &sm,const RiskPolicy &pol,const vector<string>&cands,
                             int &bestCov,int &bestSupport,int &bestNeed){
    string best;
    bestCov=bestSupport=0;
    bestNeed=1000000000;
    int bestMargin=-1000000000;
    for(const string&q:cands){
        if(sm.grams.count(q) || sm.isCovered(q)) continue;
        int sup=getv(sm.subCAll,q,0);
        if(sup>=K || sup>4) continue;
        int need=riskThreshold(pol,q,sup);
        if(need>=1000000000) continue;
        int cov=getv(sm.fsubcnt,q,0);
        if(cov<need) continue;
        int margin=cov-need;

        bool take=false;
        if(best.empty()) take=true;
        else if(pol.margin_rank){
            if(margin>bestMargin) take=true;
            else if(margin==bestMargin && sup<bestSupport) take=true;
            else if(margin==bestMargin && sup==bestSupport && cov>bestCov) take=true;
            else if(margin==bestMargin && sup==bestSupport && cov==bestCov && q.size()<best.size()) take=true;
            else if(margin==bestMargin && sup==bestSupport && cov==bestCov && q.size()==best.size() && q<best) take=true;
        }else{
            // Deliberately identical to chooseDynamicCert when all thresholds
            // are uniform, so uniform8_control is an exact harness regression.
            if(cov>bestCov) take=true;
            else if(cov==bestCov && sup<bestSupport) take=true;
            else if(cov==bestCov && sup==bestSupport && q.size()<best.size()) take=true;
            else if(cov==bestCov && sup==bestSupport && q.size()==best.size() && q<best) take=true;
        }
        if(take){
            best=q; bestCov=cov; bestSupport=sup; bestNeed=need; bestMargin=margin;
        }
    }
    return best;
}

static void processRiskCert(Sim &sm,const string&q,int cov,int support,RiskStats &rs){
    int p0=rs.all.probes,c0=rs.all.closed,s0=rs.all.sat,f0=rs.all.fresh,r0=rs.all.pruned;
    processDynamicCert(sm,q,cov,support,rs.all);
    RiskLenStats &ls=(q.size()==2?rs.len2:rs.len3);
    ls.probes += rs.all.probes-p0;
    ls.closed += rs.all.closed-c0;
    ls.sat += rs.all.sat-s0;
    ls.fresh += rs.all.fresh-f0;
    ls.pruned += rs.all.pruned-r0;
}

static pair<Result,RiskStats> runRisk(World&w,const RiskPolicy&pol,const vector<string>&cands){
    Policy p=v1(); p.name="risk-period-cert";
    // Debt thresholds stay frozen.  This experiment changes only certificate
    // eligibility/ranking; it does not retune the ordinary scheduler.
    p.debt_enter_ratio=.35;
    p.debt_exit_ratio=.18;
    p.debt_enter_floor=0;
    p.debt_exit_floor=0;
    p.debt_hard=1000000000;

    Sim sm(w,p,"learnedprune");
    RiskStats rs;
    while(!sm.active.empty() && sm.req<1000000){
        while(sm.inferSweep()){}
        sm.updateDebt();
        if(sm.active.empty()) break;

        int cov=0,sup=0,need=0;
        string cert=chooseRiskCert(sm,pol,cands,cov,sup,need);
        if(!cert.empty()){
            processRiskCert(sm,cert,cov,sup,rs);
            continue;
        }

        bool h=sm.harvestMode();
        int id=h?sm.choose(true):sm.chooseLearnedPrune();
        if(id<0) break;
        sm.processQ(id,h);
        sm.turn++;
    }
    return {finishResult(sm),rs};
}

static void printRisk(const RiskPolicy&pol,const Result&r,const RiskStats&rs,int reference){
    const auto &ds=rs.all;
    cout<<fixed<<setprecision(6)
        <<"RISK policy="<<pol.name
        <<" queries="<<r.queries
        <<" delta="<<(r.queries-reference)
        <<" probes="<<ds.probes
        <<" probe_closed="<<ds.closed
        <<" probe_sat="<<ds.sat
        <<" probe_fresh="<<ds.fresh
        <<" probe_pruned="<<ds.pruned
        <<" len2_probes="<<rs.len2.probes
        <<" len2_closed="<<rs.len2.closed
        <<" len2_sat="<<rs.len2.sat
        <<" len2_pruned="<<rs.len2.pruned
        <<" len3_probes="<<rs.len3.probes
        <<" len3_closed="<<rs.len3.closed
        <<" len3_sat="<<rs.len3.sat
        <<" len3_pruned="<<rs.len3.pruned
        <<" mean_selected_cov="<<(ds.probes?ds.cov_sum/(double)ds.probes:0.0)
        <<" mean_selected_support="<<(ds.probes?ds.support_sum/(double)ds.probes:0.0)
        <<" q99="<<r.q99
        <<" endgame="<<r.endgame
        <<" complete="<<(r.complete?1:0)
        <<"\n";
}

int main(int argc,char**argv){
    if(argc<2){cerr<<"usage: period_risk_certificate WORLD.tsv\n";return 2;}
    World w=loadWorld(argv[1]);
    const vector<string> cands=buildPeriodCandidates();

    const DynPolicy frozen{"cov8_s4",8,4};
    auto [ref,refds]=runDynamic(w,frozen,cands);
    if(!ref.complete){cerr<<"frozen reference incomplete\n";return 3;}
    cout<<fixed<<setprecision(6)
        <<"RISK_REF policy=cov8_s4 queries="<<ref.queries
        <<" probes="<<refds.probes
        <<" probe_closed="<<refds.closed
        <<" probe_sat="<<refds.sat
        <<" probe_pruned="<<refds.pruned
        <<" q99="<<ref.q99
        <<" endgame="<<ref.endgame
        <<" complete=1\n";

    const int X=1000000000;
    const array<int,5> off={X,X,X,X,X};
    const array<int,5> u8={8,8,8,8,8};
    const vector<RiskPolicy> policies={
        {"uniform8_control",u8,u8,false},
        {"xy_s0_2",off,{2,8,8,8,8},false},
        {"xy_24888",off,{2,4,8,8,8},false},
        {"xy_246810",off,{2,4,6,8,10},false},
        {"xy_23468",off,{2,3,4,6,8},false},
        {"xy_34468",off,{3,4,4,6,8},false},
        {"xy_flat4",off,{4,4,4,4,4},false},
        {"xy_22346",off,{2,2,3,4,6},false},
        {"mixed_safe",{16,16,20,24,32},{2,3,4,6,8},false},
        {"mixed_mid",{12,16,20,24,32},{2,4,6,8,10},false},
        {"xy_23468_margin",off,{2,3,4,6,8},true}
    };

    for(const auto &pol:policies){
        auto [r,rs]=runRisk(w,pol,cands);
        printRisk(pol,r,rs,ref.queries);
        if(!r.complete) return 4;
        if(pol.name=="uniform8_control" && r.queries!=ref.queries){
            cerr<<"uniform8 control diverged from frozen reference\n";
            return 5;
        }
    }
    return 0;
}
