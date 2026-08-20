#define main tag_gremlin_native_main
#include "native_sim_period_only.cpp"
#undef main

#include <iomanip>

struct DynPolicy {
    string name;
    int min_cov;
    int max_support;
};

struct DynStats {
    int probes=0, closed=0, sat=0, fresh=0, pruned=0;
    int pruned_peak=0;
    long long support_sum=0, cov_sum=0;
};

static vector<string> buildPeriodCandidates(){
    const string A="abcdefghijklmnopqrstuvwxyz0123456789";
    const string B="abcdefghijklmnopqrstuvwxyz0123456789.";
    vector<string> out;
    out.reserve(36+36*37);
    for(char a:A){
        string q="."; q+=a; out.push_back(q);
    }
    for(char a:A) for(char b:B){
        string q="."; q+=a; q+=b;
        if(q.find("..")!=string::npos) continue;
        out.push_back(move(q));
    }
    return out;
}

static string chooseDynamicCert(Sim &sm,const DynPolicy &pol,const vector<string>&cands,int &bestCov,int &bestSupport){
    string best;
    bestCov=0; bestSupport=0;
    for(const string&q:cands){
        if(sm.grams.count(q) || sm.isCovered(q)) continue;
        int cov=getv(sm.fsubcnt,q,0);
        if(cov<pol.min_cov) continue;
        int sup=getv(sm.subCAll,q,0);
        if(sup>=K || sup>pol.max_support) continue;
        if(best.empty() || cov>bestCov || (cov==bestCov && sup<bestSupport) ||
           (cov==bestCov && sup==bestSupport && q.size()<best.size()) ||
           (cov==bestCov && sup==bestSupport && q.size()==best.size() && q<best)){
            best=q; bestCov=cov; bestSupport=sup;
        }
    }
    return best;
}

static void processDynamicCert(Sim &sm,const string&q,int activeCov,int support,DynStats &ds){
    int f0=sm.frontierSize();
    int before=(int)sm.knownNames.size();
    auto qr=sm.w.query(q);
    int count=qr.first;
    sm.req++;
    if(qr.second) for(int j=0;j<count;j++) sm.addTag((*qr.second)[j]);
    int fresh=(int)sm.knownNames.size()-before;
    sm.addGram(q,count,fresh);
    sm.processed++;
    sm.yieldE=sm.processed==1?fresh:sm.yieldE*.82+fresh*.18;
    sm.depthSum+=q.size();

    int pruned=0;
    if(count<K){
        sm.closedq++;
        pruned=sm.pruneClosed(q);
        sm.noteFront(f0,1);
        if(fresh==0 && pruned==0) sm.redundant++;
        int eff=max(1,f0-sm.frontierSize());
        sm.pruneEff=sm.pruneSamples?sm.pruneEff*.88+eff*.12:eff;
        sm.pruneSamples++;
        ds.closed++;
    }else{
        // External certificate queries deliberately do NOT spawn a period-root
        // prefix tree.  Arbitrary substring queries let us ask useful grams
        // directly, so recursive descent would only add traversal overhead.
        sm.satq++;
        sm.noteFront(f0,1);
        ds.sat++;
    }

    ds.probes++;
    ds.fresh+=fresh;
    ds.pruned+=pruned;
    ds.pruned_peak=max(ds.pruned_peak,pruned);
    ds.support_sum+=support;
    ds.cov_sum+=activeCov;

    sm.discovery.push_back(sm.knownNames.size());
    sm.area+=sm.frontierSize();
    sm.turn++;
}

static Result finishResult(Sim &sm){
    Result r;
    r.queries=sm.req;
    r.found=(int)sm.knownNames.size();
    r.complete=(r.found==(int)sm.w.tags.size() && sm.active.empty());
    r.peak=sm.frontPeak;
    r.area=sm.area;
    r.inferred=sm.inferred;
    r.closedq=sm.closedq;
    r.satq=sm.satq;
    r.redundant=sm.redundant;
    r.meanDepth=sm.req?sm.depthSum/(double)sm.req:0;
    auto qt=[&](double f){
        int tar=(int)ceil(sm.w.tags.size()*f);
        for(int i=0;i<(int)sm.discovery.size();++i) if(sm.discovery[i]>=tar) return i+1;
        return -1;
    };
    r.q50=qt(.5); r.q90=qt(.9); r.q99=qt(.99);
    r.endgame=r.q99<0?-1:r.queries-r.q99;
    return r;
}

static pair<Result,DynStats> runDynamic(World&w,const DynPolicy&pol,const vector<string>&cands){
    Policy p=v1(); p.name="dynamic-period-cert";
    Sim sm(w,p,"learnedprune");
    DynStats ds;
    while(!sm.active.empty() && sm.req<1000000){
        while(sm.inferSweep()){}
        sm.updateDebt();
        if(sm.active.empty()) break;

        int cov=0,sup=0;
        string cert=chooseDynamicCert(sm,pol,cands,cov,sup);
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
    return {finishResult(sm),ds};
}

int main(int argc,char**argv){
    if(argc<2){cerr<<"usage: period_dynamic_certificate WORLD.tsv\n";return 2;}
    World w=loadWorld(argv[1]);
    Policy p=v1(); p.name="learnedprune";
    Sim base(w,p,"learnedprune");
    Result br=base.run();
    if(!br.complete){cerr<<"baseline incomplete\n";return 3;}
    cout<<"DYN_BASE queries="<<br.queries<<" closed="<<br.closedq<<" sat="<<br.satq
        <<" inferred="<<br.inferred<<" complete=1\n";

    vector<string> cands=buildPeriodCandidates();
    vector<DynPolicy> policies={
        {"cov4_s0",4,0},
        {"cov8_s0",8,0},
        {"cov8_s4",8,4},
        {"cov12_s8",12,8},
        {"cov12_s39",12,39},
        {"cov16_s16",16,16},
        {"cov24_s39",24,39}
    };
    for(auto &pol:policies){
        auto [r,ds]=runDynamic(w,pol,cands);
        cout<<"DYN policy="<<pol.name
            <<" queries="<<r.queries
            <<" delta="<<(r.queries-br.queries)
            <<" probes="<<ds.probes
            <<" probe_closed="<<ds.closed
            <<" probe_sat="<<ds.sat
            <<" probe_fresh="<<ds.fresh
            <<" probe_pruned="<<ds.pruned
            <<" probe_pruned_peak="<<ds.pruned_peak
            <<" mean_selected_cov="<<(ds.probes?ds.cov_sum/(double)ds.probes:0.0)
            <<" mean_selected_support="<<(ds.probes?ds.support_sum/(double)ds.probes:0.0)
            <<" closed="<<r.closedq
            <<" sat="<<r.satq
            <<" inferred="<<r.inferred
            <<" q99="<<r.q99
            <<" endgame="<<r.endgame
            <<" complete="<<(r.complete?1:0)
            <<"\n";
        if(!r.complete) return 4;
    }
    return 0;
}
