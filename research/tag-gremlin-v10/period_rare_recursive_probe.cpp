#define main tag_gremlin_native_main
#include "native_sim_period_only.cpp"
#undef main

#include <deque>

struct RR {
    Result r;
    int probes=0, closed=0, sat=0, fresh=0, pruned=0;
    array<int,8> depthQ{}, depthClosed{}, depthSat{};
};

static int probeQ(Sim &sm,const string &q,RR &o){
    if(q.empty() || q.find("..")!=string::npos) return -1;
    int f0=sm.frontierSize();
    int before=(int)sm.knownNames.size();
    auto qr=sm.w.query(q);
    int n=qr.first;
    sm.req++;
    if(qr.second) for(int j=0;j<n;j++) sm.addTag((*qr.second)[j]);
    int fresh=(int)sm.knownNames.size()-before;
    o.probes++; o.fresh+=fresh;
    if(q.size()<o.depthQ.size()) o.depthQ[q.size()]++;
    sm.addGram(q,n,fresh);
    sm.processed++;
    sm.yieldE=sm.processed==1?fresh:sm.yieldE*.82+fresh*.18;
    sm.depthSum+=q.size();
    if(n<K){
        o.closed++; sm.closedq++;
        if(q.size()<o.depthClosed.size()) o.depthClosed[q.size()]++;
        int k=sm.pruneClosed(q); o.pruned+=k;
        sm.noteFront(f0,1);
        if(fresh==0&&k==0) sm.redundant++;
        int eff=max(1,f0-sm.frontierSize());
        sm.pruneEff=sm.pruneSamples?sm.pruneEff*.88+eff*.12:eff;
        sm.pruneSamples++;
    }else{
        o.sat++; sm.satq++;
        if(q.size()<o.depthSat.size()) o.depthSat[q.size()]++;
        sm.noteFront(f0,1);
    }
    sm.discovery.push_back(sm.knownNames.size());
    sm.area+=sm.frontierSize();
    sm.turn++;
    while(sm.inferSweep()){}
    return n;
}

static RR runRecursive(World &w,const string &firstChars,int maxDepth){
    Policy p=v1(); p.name="rare-period-recursive";
    Sim sm(w,p,"learnedprune");
    RR out;

    int root=probeQ(sm,".",out);
    if(root>=K && maxDepth>=2){
        deque<string> todo;
        for(char c:firstChars) todo.push_back(string(".")+c);
        while(!todo.empty()){
            string q=move(todo.front()); todo.pop_front();
            if((int)q.size()>maxDepth || q.find("..")!=string::npos || sm.isCovered(q)) continue;
            int n=probeQ(sm,q,out);
            if(n>=K && (int)q.size()<maxDepth){
                for(char c:NEXT){
                    string x=q+string(1,c);
                    if(x.find("..")!=string::npos) continue;
                    todo.push_back(move(x));
                }
            }
        }
    }
    out.r=sm.run();
    return out;
}

static void show(const string &name,const RR &x,const Result &b){
    cout<<"RARE policy="<<name
        <<" queries="<<x.r.queries
        <<" delta="<<(x.r.queries-b.queries)
        <<" probes="<<x.probes
        <<" probe_closed="<<x.closed
        <<" probe_sat="<<x.sat
        <<" probe_fresh="<<x.fresh
        <<" probe_pruned="<<x.pruned
        <<" d1q="<<x.depthQ[1]<<" d1c="<<x.depthClosed[1]<<" d1s="<<x.depthSat[1]
        <<" d2q="<<x.depthQ[2]<<" d2c="<<x.depthClosed[2]<<" d2s="<<x.depthSat[2]
        <<" d3q="<<x.depthQ[3]<<" d3c="<<x.depthClosed[3]<<" d3s="<<x.depthSat[3]
        <<" d4q="<<x.depthQ[4]<<" d4c="<<x.depthClosed[4]<<" d4s="<<x.depthSat[4]
        <<" closed="<<x.r.closedq
        <<" sat="<<x.r.satq
        <<" inferred="<<x.r.inferred
        <<" complete="<<(x.r.complete?1:0)
        <<"\n";
}

int main(int argc,char **argv){
    if(argc<2){cerr<<"usage: period_rare_recursive_probe WORLD.tsv\n";return 2;}
    World w=loadWorld(argv[1]);
    Policy p=v1(); p.name="learnedprune";
    Sim base(w,p,"learnedprune");
    Result br=base.run();
    if(!br.complete){cerr<<"baseline incomplete\n";return 3;}
    cout<<"RARE_BASE queries="<<br.queries<<" closed="<<br.closedq<<" sat="<<br.satq<<" inferred="<<br.inferred<<" complete=1\n";

    struct C { string name,chars; int depth; };
    vector<C> cases={
        {"digits_d3","0123456789",3},
        {"qxz_digits_d2","qxz0123456789",2},
        {"qvxz_digits_d2","qvxz0123456789",2},
        {"qxz_digits_d3","qxz0123456789",3},
        {"qvxz_digits_d3","qvxz0123456789",3},
        {"qvxz_digits_d4","qvxz0123456789",4},
    };
    for(auto &c:cases){
        RR x=runRecursive(w,c.chars,c.depth);
        show(c.name,x,br);
        if(!x.r.complete) return 4;
    }
    return 0;
}
