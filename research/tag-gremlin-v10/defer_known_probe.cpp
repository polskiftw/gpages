#define main tag_gremlin_native_main
#include "native_sim.cpp"
#undef main

#include <iomanip>

struct DeferResult{
    int queries=0,inferred=0,closedq=0,satq=0;
    long long skipped_candidates=0;
    int fallback_turns=0;
    bool complete=false;
};

static int chooseFiltered(Sim &sm,bool harvest,int threshold,long long &skipped,int &fallbacks){
    int mn=1;
    while(mn<(int)sm.activeLen.size() && sm.activeLen[mn].empty()) mn++;
    if(mn>=(int)sm.activeLen.size()) return -1;
    int horizon;
    if(harvest) horizon=mn+(sm.yieldE>=6?sm.p.harvest_h3:sm.yieldE>=2?sm.p.harvest_h2:sm.p.harvest_h1);
    else horizon=(sm.debt&&!sm.p.debt_mode_continuous)?mn:mn+1;
    horizon=min(horizon,(int)sm.activeLen.size()-1);

    int best=-1; double bs=-1e300;
    for(int L=mn;L<=horizon;L++) for(int id:sm.activeLen[L]){
        auto &q=sm.cs[id].q;
        int d=getv(sm.subCAll,q,0);
        bool defer=sm.knownNames.count(q) && d>=threshold && d<K;
        if(defer){ skipped++; continue; }
        double score=harvest?sm.hs(id):sm.learnedPruneScore(id,mn);
        if(score>bs){bs=score;best=id;}
    }
    if(best>=0) return best;
    fallbacks++;
    return harvest?sm.choose(true):sm.chooseLearnedPrune();
}

static DeferResult runDefer(World &w,int threshold){
    Policy p=v1(); p.name="defer-known";
    Sim sm(w,p,"learnedprune");
    long long skipped=0; int fallbacks=0;
    while(!sm.active.empty() && sm.req<1000000){
        while(sm.inferSweep()){}
        sm.updateDebt();
        if(sm.active.empty()) break;
        bool h=sm.harvestMode();
        int id=chooseFiltered(sm,h,threshold,skipped,fallbacks);
        if(id<0) break;
        sm.processQ(id,h); sm.turn++;
    }
    return {sm.req,sm.inferred,sm.closedq,sm.satq,skipped,fallbacks,
            sm.knownNames.size()==w.tags.size() && sm.active.empty()};
}

int main(int argc,char **argv){
    if(argc<2){cerr<<"usage: defer_known_probe WORLD.tsv\n";return 2;}
    World w=loadWorld(argv[1]);
    Policy p=v1(); p.name="learnedprune";
    Sim base(w,p,"learnedprune");
    Result br=base.run();
    if(!br.complete){cerr<<"baseline incomplete\n";return 3;}
    cout<<"DEFER_BASE queries="<<br.queries<<" inferred="<<br.inferred<<" closed="<<br.closedq<<" sat="<<br.satq<<"\n";
    for(int th: {20,30,35,38,39}){
        DeferResult r=runDefer(w,th);
        cout<<"DEFER threshold="<<th
            <<" queries="<<r.queries
            <<" delta="<<(r.queries-br.queries)
            <<" inferred="<<r.inferred
            <<" inferred_delta="<<(r.inferred-br.inferred)
            <<" closed="<<r.closedq
            <<" sat="<<r.satq
            <<" skipped_candidates="<<r.skipped_candidates
            <<" fallback_turns="<<r.fallback_turns
            <<" complete="<<(r.complete?1:0)
            <<"\n";
        if(!r.complete) return 4;
    }
    return 0;
}
