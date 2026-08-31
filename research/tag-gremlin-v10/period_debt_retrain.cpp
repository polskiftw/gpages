#define TAG_GREMLIN_DYNAMIC_NO_MAIN
#include "period_dynamic_certificate.cpp"

#include <iomanip>

struct DebtRun {
    Result result;
    DynStats dyn;
    int debt_entries=0;
};

static DebtRun runDebt(World &w,const DynPolicy &dpol,const vector<string>&cands,double enter,double exit){
    Policy p=v1();
    p.name="dynamic-period-cert-debt";
    p.debt_enter_ratio=enter;
    p.debt_exit_ratio=exit;
    p.debt_enter_floor=0;
    p.debt_exit_floor=0;
    p.debt_hard=1000000000;

    Sim sm(w,p,"learnedprune");
    DynStats ds;
    while(!sm.active.empty() && sm.req<1000000){
        while(sm.inferSweep()){}
        sm.updateDebt();
        if(sm.active.empty()) break;

        int cov=0,sup=0;
        string cert=chooseDynamicCert(sm,dpol,cands,cov,sup);
        if(!cert.empty()){
            processDynamicCert(sm,cert,cov,sup,ds);
            continue;
        }

        bool h=sm.harvestMode();
        int id=h?sm.choose(true):sm.chooseLearnedPrune();
        if(id<0) break;
        sm.processQ(id,h);
        sm.turn++;
    }
    return {finishResult(sm),ds,sm.debtEntries};
}

static void printDebt(const char *label,double enter,double exit,const DebtRun &dr,int reference){
    const Result&r=dr.result;
    const DynStats&ds=dr.dyn;
    cout<<fixed<<setprecision(3)
        <<label
        <<" enter="<<enter
        <<" exit="<<exit
        <<" queries="<<r.queries
        <<" delta="<<(r.queries-reference)
        <<" debt_entries="<<dr.debt_entries
        <<" probes="<<ds.probes
        <<" probe_closed="<<ds.closed
        <<" probe_sat="<<ds.sat
        <<" probe_pruned="<<ds.pruned
        <<" inferred="<<r.inferred
        <<" q99="<<r.q99
        <<" endgame="<<r.endgame
        <<" frontier_peak="<<r.peak
        <<" complete="<<(r.complete?1:0)
        <<"\n";
}

int main(int argc,char**argv){
    if(argc<2){cerr<<"usage: period_debt_retrain WORLD.tsv\n";return 2;}
    World w=loadWorld(argv[1]);
    const DynPolicy dpol{"cov8_s4",8,4};
    const vector<string> cands=buildPeriodCandidates();

    // Current production thresholds are the frozen reference.  The grid is
    // deliberately trained only after the certificate policy itself was frozen.
    DebtRun base=runDebt(w,dpol,cands,.35,.18);
    if(!base.result.complete){cerr<<"reference incomplete\n";return 3;}
    printDebt("DEBT_BASE",.35,.18,base,base.result.queries);

    // Two useful extremes answer whether hysteretic debt mode is still needed
    // at all after direct CLOSED-certificate pruning was added.
    DebtRun always=runDebt(w,dpol,cands,0.0,0.0);
    if(!always.result.complete) return 4;
    printDebt("DEBT_EXTREME",0.0,0.0,always,base.result.queries);

    DebtRun never=runDebt(w,dpol,cands,100.0,99.0);
    if(!never.result.complete) return 5;
    printDebt("DEBT_EXTREME",100.0,99.0,never,base.result.queries);

    const vector<double> enters={.20,.25,.30,.35,.40,.50,.60};
    const vector<double> exits={.08,.12,.18,.24,.30};
    for(double enter:enters){
        for(double exit:exits){
            // Keep real hysteresis; the .35/.18 reference is repeated in the
            // grid intentionally so aggregation scripts can treat every row the
            // same without special casing DEBT_BASE.
            if(exit+0.04>enter) continue;
            DebtRun dr=runDebt(w,dpol,cands,enter,exit);
            if(!dr.result.complete){
                printDebt("DEBT",enter,exit,dr,base.result.queries);
                return 6;
            }
            printDebt("DEBT",enter,exit,dr,base.result.queries);
        }
    }
    return 0;
}
