#define main tag_gremlin_native_main
#include "native_sim.cpp"
#undef main

#include <chrono>
#include <iomanip>

struct CachedAnswer {
    int count=0;
    vector<int> ids;
};

struct OracleCache {
    World &w;
    unordered_map<string,CachedAnswer> cache;
    explicit OracleCache(World &ww):w(ww){ cache.reserve(65536); }
    const CachedAnswer& get(const string &q){
        auto it=cache.find(q);
        if(it!=cache.end()) return it->second;
        auto qr=w.query(q);
        CachedAnswer a; a.count=qr.first;
        if(qr.second) for(int i=0;i<qr.first;i++) a.ids.push_back((*qr.second)[i]);
        return cache.emplace(q,move(a)).first->second;
    }
    int fresh(Sim &sm,const CachedAnswer &a) const {
        int n=0; for(int id:a.ids) if(!sm.knownTag[id]) n++; return n;
    }
};

static int minLen(Sim &sm){
    int mn=1; while(mn<(int)sm.activeLen.size() && sm.activeLen[mn].empty()) mn++;
    return mn;
}

static int oracleProof(Sim &sm,OracleCache &oc){
    int mn=minLen(sm); if(mn>=(int)sm.activeLen.size()) return -1;
    int horizon=(sm.debt&&!sm.p.debt_mode_continuous)?mn:mn+1;
    horizon=min(horizon,(int)sm.activeLen.size()-1);
    int best=-1; long long bestClosed=-1; int bestFresh=-1; double bestBase=-1e300;
    for(int L=mn;L<=horizon;L++) for(int id:sm.activeLen[L]){
        auto &q=sm.cs[id].q; const auto &a=oc.get(q);
        int fr=oc.fresh(sm,a);
        long long closedGain=(a.count<K)?max(1,getv(sm.fsubcnt,q,1)):0;
        double base=sm.learnedPruneScore(id,mn);
        if(closedGain>bestClosed ||
           (closedGain==bestClosed && fr>bestFresh) ||
           (closedGain==bestClosed && fr==bestFresh && base>bestBase)){
            best=id; bestClosed=closedGain; bestFresh=fr; bestBase=base;
        }
    }
    return best;
}

static int oracleHarvest(Sim &sm,OracleCache &oc){
    int mn=minLen(sm); if(mn>=(int)sm.activeLen.size()) return -1;
    int horizon=mn+(sm.yieldE>=6?sm.p.harvest_h3:sm.yieldE>=2?sm.p.harvest_h2:sm.p.harvest_h1);
    horizon=min(horizon,(int)sm.activeLen.size()-1);
    int best=-1,bestFresh=-1; long long bestClosed=-1; double bestHs=-1e300;
    for(int L=mn;L<=horizon;L++) for(int id:sm.activeLen[L]){
        auto &q=sm.cs[id].q; const auto &a=oc.get(q);
        int fr=oc.fresh(sm,a);
        long long closedGain=(a.count<K)?max(1,getv(sm.fsubcnt,q,1)):0;
        double hs=sm.hs(id);
        if(fr>bestFresh ||
           (fr==bestFresh && closedGain>bestClosed) ||
           (fr==bestFresh && closedGain==bestClosed && hs>bestHs)){
            best=id; bestFresh=fr; bestClosed=closedGain; bestHs=hs;
        }
    }
    return best;
}

static Result runOracle(World &w,const string &mode,double &seconds){
    Policy p=v1(); p.name=mode;
    Sim sm(w,p,"learnedprune");
    OracleCache oc(w);
    auto t0=chrono::steady_clock::now();
    while(!sm.active.empty() && sm.req<1000000){
        while(sm.inferSweep()){}
        sm.updateDebt();
        if(sm.active.empty()) break;
        bool h=sm.harvestMode();
        int id=-1;
        if(h && (mode=="oracle-harvest" || mode=="oracle-both")) id=oracleHarvest(sm,oc);
        else if(!h && (mode=="oracle-proof" || mode=="oracle-both")) id=oracleProof(sm,oc);
        else id=h?sm.choose(true):sm.chooseLearnedPrune();
        if(id<0) break;
        sm.processQ(id,h); sm.turn++;
    }
    seconds=chrono::duration<double>(chrono::steady_clock::now()-t0).count();
    Result r;
    r.queries=sm.req; r.found=sm.knownNames.size();
    r.complete=(r.found==(int)w.tags.size() && sm.active.empty());
    r.peak=sm.frontPeak; r.area=sm.area; r.inferred=sm.inferred;
    r.closedq=sm.closedq; r.satq=sm.satq; r.redundant=sm.redundant;
    r.meanDepth=sm.req?sm.depthSum/(double)sm.req:0;
    auto qt=[&](double f){int tar=ceil(w.tags.size()*f);for(int i=0;i<(int)sm.discovery.size();++i)if(sm.discovery[i]>=tar)return i+1;return -1;};
    r.q50=qt(.5); r.q90=qt(.9); r.q99=qt(.99); r.endgame=r.q99<0?-1:r.queries-r.q99;
    return r;
}

int main(int argc,char **argv){
    if(argc<2){ cerr<<"usage: oracle_ceiling WORLD.tsv\n"; return 2; }
    World w=loadWorld(argv[1]);
    Policy p=v1(); p.name="learnedprune";
    Sim base(w,p,"learnedprune");
    auto t0=chrono::steady_clock::now(); Result br=base.run();
    double bsec=chrono::duration<double>(chrono::steady_clock::now()-t0).count();
    printR("learnedprune",br,bsec);
    if(!br.complete) return 3;
    for(string mode: {string("oracle-proof"),string("oracle-harvest"),string("oracle-both")}){
        double sec=0; Result r=runOracle(w,mode,sec); printR(mode,r,sec);
        cout<<"ORACLE_DELTA mode="<<mode<<" delta="<<(r.queries-br.queries)<<" endgame_delta="<<(r.endgame-br.endgame)<<"\n";
        if(!r.complete) return 4;
    }
    return 0;
}
