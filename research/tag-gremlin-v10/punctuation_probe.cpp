#define main tag_gremlin_native_main
#include "native_sim_period_only.cpp"
#undef main

#include <deque>

struct PunctResult {
    Result result;
    int probe_queries=0, probe_closed=0, probe_sat=0;
    int probe_fresh=0, probe_pruned=0;
};

static int processProbe(Sim &sm,const string &q,int &closedN,int &satN,int &freshN,int &prunedN){
    int f0=sm.frontierSize();
    int before=(int)sm.knownNames.size();
    auto qr=sm.w.query(q);
    int count=qr.first;
    sm.req++;
    if(qr.second) for(int j=0;j<count;j++) sm.addTag((*qr.second)[j]);
    int fresh=(int)sm.knownNames.size()-before;
    freshN+=fresh;
    sm.addGram(q,count,fresh);
    sm.processed++;
    sm.yieldE=sm.processed==1?fresh:sm.yieldE*.82+fresh*.18;
    sm.depthSum+=q.size();
    if(count<K){
        sm.closedq++; closedN++;
        int n=sm.pruneClosed(q); prunedN+=n;
        sm.noteFront(f0,1);
        if(fresh==0&&n==0) sm.redundant++;
        int eff=max(1,f0-sm.frontierSize());
        sm.pruneEff=sm.pruneSamples?sm.pruneEff*.88+eff*.12:eff;
        sm.pruneSamples++;
    } else {
        sm.satq++; satN++;
        sm.noteFront(f0,1);
    }
    sm.discovery.push_back(sm.knownNames.size());
    sm.area+=sm.frontierSize();
    sm.turn++;
    return count;
}

static PunctResult runPunct(World &w,int maxDepth){
    Policy p=v1(); p.name="period-root";
    Sim sm(w,p,"learnedprune");
    deque<string> todo;
    todo.push_back(".");
    int pq=0,pc=0,ps=0,pf=0,pp=0;
    while(!todo.empty()){
        string q=move(todo.front()); todo.pop_front();
        if(sm.isCovered(q)) continue;
        int count=processProbe(sm,q,pc,ps,pf,pp); pq++;
        while(sm.inferSweep()){}
        if(count>=K && (int)q.size()<maxDepth){
            for(char c:NEXT) todo.push_back(q+string(1,c));
        }
    }
    Result tail=sm.run();
    return {tail,pq,pc,ps,pf,pp};
}

int main(int argc,char **argv){
    if(argc<2){cerr<<"usage: punctuation_probe WORLD.tsv\n";return 2;}
    World w=loadWorld(argv[1]);
    Policy p=v1(); p.name="learnedprune";
    Sim base(w,p,"learnedprune");
    Result br=base.run();
    if(!br.complete){cerr<<"baseline incomplete\n";return 3;}
    cout<<"PERIOD_BASE queries="<<br.queries<<" closed="<<br.closedq<<" sat="<<br.satq<<" inferred="<<br.inferred<<" complete=1\n";
    for(int d: {1,2,3,4}){
        auto x=runPunct(w,d);
        auto &r=x.result;
        cout<<"PERIOD depth="<<d
            <<" queries="<<r.queries
            <<" delta="<<(r.queries-br.queries)
            <<" closed="<<r.closedq
            <<" closed_delta="<<(r.closedq-br.closedq)
            <<" sat="<<r.satq
            <<" inferred="<<r.inferred
            <<" probe_queries="<<x.probe_queries
            <<" probe_closed="<<x.probe_closed
            <<" probe_sat="<<x.probe_sat
            <<" probe_fresh="<<x.probe_fresh
            <<" probe_pruned="<<x.probe_pruned
            <<" complete="<<(r.complete?1:0)
            <<"\n";
        if(!r.complete) return 4;
    }
    return 0;
}
