#define main tag_gremlin_native_main
#include "native_sim.cpp"
#undef main

#include <iomanip>

static Result runAdaptive(World &w,double threshold,int harvestN,int proofN){
    Policy p=v1(); p.name="adaptive";
    Sim sm(w,p,"learnedprune");
    int phase=0;
    const int cycle=max(1,harvestN+proofN);
    while(!sm.active.empty() && sm.req<1000000){
        while(sm.inferSweep()){}
        sm.updateDebt();
        if(sm.active.empty()) break;
        bool h=false;
        int id=-1;
        if(sm.debt && sm.fratio()<=threshold){
            h=(phase%cycle)<harvestN;
            id=h?sm.choose(true):sm.chooseLearnedPrune();
            phase++;
        }else{
            phase=0;
            h=sm.harvestMode();
            id=h?sm.choose(true):sm.chooseLearnedPrune();
        }
        if(id<0) break;
        sm.processQ(id,h); sm.turn++;
    }
    Result r;
    r.policy="adaptive"; r.queries=sm.req; r.complete=sm.knownNames.size()==w.tags.size();
    r.harvest=sm.hq; r.prune=sm.pq; r.sat=sm.satq; r.closed=sm.closedq; r.inferred=sm.inferred;
    r.frontPeak=sm.frontPeak; r.pruneEff=sm.pruneEff;
    return r;
}

int main(int argc,char **argv){
    if(argc<2){cerr<<"usage: adaptive_mix_probe WORLD.tsv\n";return 2;}
    World w=loadWorld(argv[1]);
    Policy p=v1(); p.name="learnedprune";
    Sim base(w,p,"learnedprune");
    Result br=base.run();
    if(!br.complete){cerr<<"baseline incomplete\n";return 3;}
    cout<<"ADAPTIVE baseline="<<br.queries<<" complete=1\n";
    struct V{double t;int h,p;const char*name;};
    const vector<V> vars={
        {1.10,3,1,"primary-t1.10-h3p1"},
        {0.90,3,1,"diag-t0.90-h3p1"},
        {1.30,3,1,"diag-t1.30-h3p1"},
        {1.10,1,1,"diag-t1.10-h1p1"}
    };
    for(auto v:vars){
        Result r=runAdaptive(w,v.t,v.h,v.p);
        cout<<fixed<<setprecision(2)
            <<"ADAPTIVE name="<<v.name
            <<" threshold="<<v.t
            <<" h="<<v.h<<" p="<<v.p
            <<" queries="<<r.queries
            <<" delta="<<(r.queries-br.queries)
            <<" complete="<<(r.complete?1:0)
            <<" harvest="<<r.harvest
            <<" prune="<<r.prune
            <<" inferred="<<r.inferred
            <<"\n";
        if(!r.complete) return 4;
    }
    return 0;
}
