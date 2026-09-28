#define TAG_GREMLIN_DYNAMIC_NO_MAIN
#include "period_dynamic_certificate.cpp"

#include <queue>
#include <iomanip>

struct TruthCounts {
    unordered_map<string,uint8_t> count;
};

static TruthCounts buildTruth(const World&w,int maxAfter){
    TruthCounts tc;
    tc.count.reserve(w.tags.size()*2);
    for(const string&tag:w.tags){
        unordered_set<string> seen;
        for(int i=0;i<(int)tag.size();++i){
            if(tag[i]!='.') continue;
            for(int after=1;after<=maxAfter;++after){
                int z=i+1+after;
                if(z>(int)tag.size()) break;
                seen.insert(tag.substr(i,z-i));
            }
        }
        for(const string&q:seen){
            auto &v=tc.count[q];
            if(v<K) ++v;
        }
    }
    return tc;
}

struct HeapEntry {
    int cov;
    string q;
};
struct HeapCmp {
    bool operator()(HeapEntry const&a,HeapEntry const&b)const{
        if(a.cov!=b.cov) return a.cov<b.cov;
        if(a.q.size()!=b.q.size()) return a.q.size()>b.q.size();
        return a.q>b.q;
    }
};

struct OracleHeap {
    const TruthCounts &truth;
    int maxAfter;
    priority_queue<HeapEntry,vector<HeapEntry>,HeapCmp> pq;

    OracleHeap(const TruthCounts&t,int m):truth(t),maxAfter(m){}

    bool trulyClosed(const string&q)const{
        auto it=truth.count.find(q);
        int n=(it==truth.count.end()?0:(int)it->second);
        return n<K;
    }

    void noteCandidate(Sim&sm,const string&q){
        if(!trulyClosed(q)) return;
        int cov=getv(sm.fsubcnt,q,0);
        if(cov>1) pq.push({cov,q});
    }

    void noteFrontierString(Sim&sm,const string&s){
        for(int i=0;i<(int)s.size();++i){
            if(s[i]!='.') continue;
            for(int after=1;after<=maxAfter;++after){
                int z=i+1+after;
                if(z>(int)s.size()) break;
                noteCandidate(sm,s.substr(i,z-i));
            }
        }
    }

    void noteNew(Sim&sm,size_t oldSize){
        for(size_t i=oldSize;i<sm.cs.size();++i) noteFrontierString(sm,sm.cs[i].q);
    }

    string choose(Sim&sm,int minCov,int &actualCov){
        while(!pq.empty()){
            auto e=pq.top(); pq.pop();
            if(sm.grams.count(e.q) || sm.isCovered(e.q)) continue;
            int cov=getv(sm.fsubcnt,e.q,0);
            if(cov<minCov) continue;
            if(cov!=e.cov){
                pq.push({cov,e.q});
                continue;
            }
            actualCov=cov;
            return e.q;
        }
        actualCov=0;
        return {};
    }
};

struct OracleRun {
    Result result;
    DynStats dyn;
};

static OracleRun runOracle(World&w,const TruthCounts&truth,int maxAfter,int minCov){
    Policy p=v1(); p.name="period-certificate-oracle";
    Sim sm(w,p,"learnedprune");
    DynStats ds;
    OracleHeap heap(truth,maxAfter);
    heap.noteNew(sm,0);

    while(!sm.active.empty() && sm.req<1000000){
        for(;;){
            size_t oldSize=sm.cs.size();
            int changed=sm.inferSweep();
            heap.noteNew(sm,oldSize);
            if(!changed) break;
        }
        sm.updateDebt();
        if(sm.active.empty()) break;

        int cov=0;
        string cert=heap.choose(sm,minCov,cov);
        if(!cert.empty()){
            int support=getv(sm.subCAll,cert,0);
            int hiddenCount=0;
            auto it=truth.count.find(cert);
            if(it!=truth.count.end()) hiddenCount=it->second;
            if(hiddenCount>=K){cerr<<"oracle selected SAT candidate\n";exit(9);}
            processDynamicCert(sm,cert,cov,support,ds);
            continue;
        }

        bool h=sm.harvestMode();
        int id=h?sm.choose(true):sm.chooseLearnedPrune();
        if(id<0) break;
        size_t oldSize=sm.cs.size();
        sm.processQ(id,h);
        heap.noteNew(sm,oldSize);
        sm.turn++;
    }
    return {finishResult(sm),ds};
}

int main(int argc,char**argv){
    if(argc<2){cerr<<"usage: period_dynamic_oracle WORLD.tsv\n";return 2;}
    World w=loadWorld(argv[1]);

    const vector<string> cands=buildPeriodCandidates();
    const DynPolicy frozen{"cov8_s4",8,4};
    auto [fr,fds]=runDynamic(w,frozen,cands);
    if(!fr.complete){cerr<<"frozen policy incomplete\n";return 3;}
    cout<<"ORACLE_REF queries="<<fr.queries
        <<" probes="<<fds.probes
        <<" probe_closed="<<fds.closed
        <<" probe_sat="<<fds.sat
        <<" probe_pruned="<<fds.pruned
        <<" q99="<<fr.q99
        <<" endgame="<<fr.endgame
        <<" complete=1\n";

    for(int maxAfter:{2,3,4}){
        TruthCounts truth=buildTruth(w,maxAfter);
        for(int minCov:{2,4,8,12}){
            OracleRun o=runOracle(w,truth,maxAfter,minCov);
            cout<<"ORACLE max_after="<<maxAfter
                <<" min_cov="<<minCov
                <<" queries="<<o.result.queries
                <<" delta_vs_frozen="<<(o.result.queries-fr.queries)
                <<" probes="<<o.dyn.probes
                <<" probe_pruned="<<o.dyn.pruned
                <<" q99="<<o.result.q99
                <<" endgame="<<o.result.endgame
                <<" complete="<<(o.result.complete?1:0)
                <<"\n";
            if(!o.result.complete) return 4;
        }
    }
    return 0;
}
