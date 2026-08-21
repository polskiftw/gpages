#define main tag_gremlin_native_main
#include "native_sim.cpp"
#undef main

#include <fstream>
#include <iomanip>

struct RolloutRow {
    int snap=0;
    double target=0;
    int req=0, known=0, frontier=0;
    double fratio=0, pressure=0, yieldE=0, deltaE=0;
    bool debt=false;
    string q;
    int depth=0, d=0, pre=0, age=0, pf=0;
    double lm=0, sy=0, ss=0, sc=0, nov=0, br=0, ctx=0, hs=0, ps=0, lp=0;
    bool baselineNext=false;
    int finalTotal=0;
    bool complete=false;
};

static vector<int> legalPool(Sim &sm){
    int mn=1;
    while(mn<(int)sm.activeLen.size() && sm.activeLen[mn].empty()) mn++;
    vector<int> out;
    if(mn>=(int)sm.activeLen.size()) return out;
    int horizon=min(mn+3,(int)sm.activeLen.size()-1);
    for(int L=mn;L<=horizon;L++) for(int id:sm.activeLen[L]) out.push_back(id);
    return out;
}

static int baselineChoice(Sim &sm){
    bool h=sm.harvestMode();
    return h?sm.choose(true):sm.chooseLearnedPrune();
}

static vector<int> selectCandidates(Sim &sm,int budget){
    vector<int> pool=legalPool(sm), out;
    unordered_set<int> seen;
    auto add=[&](int id){
        if(id>=0 && sm.cs[id].active && seen.insert(id).second && (int)out.size()<budget) out.push_back(id);
    };
    add(baselineChoice(sm));
    add(sm.chooseSoftMix());
    if(pool.empty()) return out;

    int mn=1;
    while(mn<(int)sm.activeLen.size() && sm.activeLen[mn].empty()) mn++;
    auto takeTop=[&](auto score,int n){
        vector<pair<double,int>> a; a.reserve(pool.size());
        for(int id:pool) a.push_back({score(id),id});
        int k=min(n,(int)a.size());
        partial_sort(a.begin(),a.begin()+k,a.end(),[](auto &x,auto &y){return x.first>y.first;});
        for(int i=0;i<k;i++) add(a[i].second);
    };
    takeTop([&](int id){return sm.learnedPruneScore(id,mn);}, max(2,budget/3));
    takeTop([&](int id){return sm.hs(id);}, max(2,budget/3));
    takeTop([&](int id){return sm.ps(id);}, max(2,budget/3));

    // Deterministic lexical spread adds actions that hand-written scorers may all ignore.
    sort(pool.begin(),pool.end(),[&](int a,int b){return sm.cs[a].q<sm.cs[b].q;});
    if(!pool.empty()){
        for(int j=0;(int)out.size()<budget && j<budget*2;j++){
            size_t pos=(size_t)((j+0.5)*pool.size()/max(1,budget*2));
            if(pos>=pool.size()) pos=pool.size()-1;
            add(pool[pos]);
        }
    }
    return out;
}

static RolloutRow makeRow(Sim &sm,int id,int snap,double target,int baselineId){
    auto f=sm.feat(id);
    auto &q=sm.cs[id].q;
    RolloutRow r;
    r.snap=snap; r.target=target; r.req=sm.req; r.known=sm.knownNames.size(); r.frontier=sm.frontierSize();
    r.fratio=sm.fratio(); r.pressure=sm.pressure(); r.yieldE=sm.yieldE; r.deltaE=sm.deltaE; r.debt=sm.debt;
    r.q=q; r.depth=f.dep; r.d=f.d; r.pre=f.pre; r.age=f.age; r.lm=f.lm; r.sy=f.sy; r.ss=f.ss; r.sc=f.sc; r.nov=f.nov; r.br=f.br;
    r.pf=max(1,getv(sm.fsubcnt,q,1)); r.ctx=log1p(getv(sm.ctxMass,q,0.0));
    int mn=1; while(mn<(int)sm.activeLen.size() && sm.activeLen[mn].empty()) mn++;
    r.hs=sm.hs(id); r.ps=sm.ps(id); r.lp=sm.learnedPruneScore(id,mn); r.baselineNext=(id==baselineId);
    return r;
}

static void csvHeader(ofstream &f){
    f<<"snapshot,target,req,known,frontier,fratio,pressure,yieldE,deltaE,debt,q,depth,d,pre,age,lm,sy,ss,sc,nov,br,pf,ctx,hs,ps,lp,baseline_next,final_total,delta_vs_baseline,regret_vs_best,complete\n";
}
static void csvRow(ofstream &f,const RolloutRow&r,int baselineTotal,int bestTotal){
    f<<r.snap<<','<<r.target<<','<<r.req<<','<<r.known<<','<<r.frontier<<','<<r.fratio<<','<<r.pressure<<','<<r.yieldE<<','<<r.deltaE<<','<<(r.debt?1:0)<<','<<r.q<<','<<r.depth<<','<<r.d<<','<<r.pre<<','<<r.age<<','<<r.lm<<','<<r.sy<<','<<r.ss<<','<<r.sc<<','<<r.nov<<','<<r.br<<','<<r.pf<<','<<r.ctx<<','<<r.hs<<','<<r.ps<<','<<r.lp<<','<<(r.baselineNext?1:0)<<','<<r.finalTotal<<','<<(r.finalTotal-baselineTotal)<<','<<(r.finalTotal-bestTotal)<<','<<(r.complete?1:0)<<'\n';
}

static bool evalSnapshot(Sim &sm,int snap,double target,int budget,ofstream &csv,long long &sumGain,int &maxGain,int &baselineWins){
    int bid=baselineChoice(sm);
    vector<int> cand=selectCandidates(sm,budget);
    if(bid<0 || cand.empty()) return false;

    Sim base=sm; base.repairIterators();
    Result br=base.run();
    if(!br.complete){cerr<<"baseline continuation incomplete at snapshot "<<snap<<"\n"; return false;}
    int baselineTotal=br.queries;

    vector<RolloutRow> rows;
    rows.reserve(cand.size());
    int bestTotal=INT_MAX, bestId=-1;
    for(int id:cand){
        RolloutRow row=makeRow(sm,id,snap,target,bid);
        Sim c=sm; c.repairIterators();
        if(!c.cs[id].active) continue;
        c.processQ(id,false); c.turn++;
        Result rr=c.run();
        row.finalTotal=rr.queries; row.complete=rr.complete;
        if(rr.complete && rr.queries<bestTotal){bestTotal=rr.queries; bestId=id;}
        rows.push_back(row);
    }
    if(bestId<0){cerr<<"no complete rollout at snapshot "<<snap<<"\n"; return false;}
    for(auto &r:rows) csvRow(csv,r,baselineTotal,bestTotal);

    int gain=baselineTotal-bestTotal;
    sumGain+=gain; maxGain=max(maxGain,gain); if(gain==0) baselineWins++;
    cout<<fixed<<setprecision(4)
        <<"SNAPSHOT idx="<<snap
        <<" target="<<target
        <<" req="<<sm.req
        <<" known="<<sm.knownNames.size()
        <<" frontier="<<sm.frontierSize()
        <<" fratio="<<sm.fratio()
        <<" debt="<<(sm.debt?1:0)
        <<" baseline_total="<<baselineTotal
        <<" best_total="<<bestTotal
        <<" gain="<<gain
        <<" baseline_next="<<sm.cs[bid].q
        <<" best_q="<<sm.cs[bestId].q
        <<" candidates="<<rows.size()
        <<"\n";
    return true;
}

int main(int argc,char **argv){
    if(argc<3){
        cerr<<"usage: rollout_pass3 WORLD.tsv OUT.csv [candidate_budget]\n";
        return 2;
    }
    int budget=argc>3?stoi(argv[3]):10;
    World w=loadWorld(argv[1]);
    Policy p=v1(); p.name="learnedprune";
    Sim sm(w,p,"learnedprune");
    ofstream csv(argv[2]);
    if(!csv){cerr<<"cannot open output csv\n";return 2;}
    csv<<setprecision(12); csvHeader(csv);

    const vector<double> targets={.25,.50,.75,.90,.99};
    int ti=0,snaps=0,baselineWins=0,maxGain=0;
    long long sumGain=0;
    while(!sm.active.empty() && sm.req<1000000){
        while(sm.inferSweep()){}
        sm.updateDebt();
        if(sm.active.empty()) break;
        double frac=sm.knownNames.size()/(double)w.tags.size();
        while(ti<(int)targets.size() && frac>=targets[ti]){
            if(evalSnapshot(sm,ti,targets[ti],budget,csv,sumGain,maxGain,baselineWins)) snaps++;
            ti++;
        }
        int id=baselineChoice(sm);
        if(id<0) break;
        sm.processQ(id,sm.harvestMode()); sm.turn++;
    }
    Result fin=sm.run();
    cout<<"PASS3_SUMMARY snapshots="<<snaps
        <<" sum_one_step_gain="<<sumGain
        <<" max_one_step_gain="<<maxGain
        <<" baseline_ties="<<baselineWins
        <<" final_queries="<<fin.queries
        <<" complete="<<(fin.complete?1:0)
        <<"\n";
    return (fin.complete && snaps==(int)targets.size())?0:3;
}
