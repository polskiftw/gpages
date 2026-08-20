#define main tag_gremlin_native_main
#include "native_sim_period_only.cpp"
#undef main

#include <array>
#include <cmath>

struct Obs {
    int count=0;
    int distinct=0;
    int maxSupport=0;
    double entropy=0;
    array<int,39> support{}; // a-z,0-9,'.',end,other
};

struct AR {
    Result r;
    int probes=0,closed=0,sat=0,fresh=0,pruned=0;
    int selectedParents=0;
    int d2q=0,d2c=0,d2s=0,d3q=0,d3c=0,d3s=0;
    vector<string> selected;
};

static int cslot(char c){
    if(c>='a'&&c<='z') return c-'a';
    if(c>='0'&&c<='9') return 26+(c-'0');
    if(c=='.') return 36;
    return 38;
}

static Obs observe(const World &w,const string &q,const array<int,K>* ids,int n){
    Obs o; o.count=n;
    if(!ids || n<=0) return o;
    for(int j=0;j<n;j++){
        const string &t=w.tags[(*ids)[j]];
        array<char,39> seen{};
        size_t pos=0;
        while((pos=t.find(q,pos))!=string::npos){
            size_t z=pos+q.size();
            int sl=(z<t.size())?cslot(t[z]):37; // 37=end of tag
            seen[sl]=1;
            pos++;
        }
        for(int s=0;s<39;s++) if(seen[s]) o.support[s]++;
    }
    double H=0;
    int sum=0;
    for(int x:o.support) sum+=x;
    for(int x:o.support) if(x){
        o.distinct++;
        o.maxSupport=max(o.maxSupport,x);
        double p=x/(double)max(1,sum);
        H-=p*log2(p);
    }
    o.entropy=H;
    return o;
}

static pair<int,Obs> probeQ(Sim &sm,const string &q,AR &o){
    int f0=sm.frontierSize();
    int before=(int)sm.knownNames.size();
    auto qr=sm.w.query(q);
    int n=qr.first;
    Obs ob=observe(sm.w,q,qr.second,n);
    sm.req++;
    if(qr.second) for(int j=0;j<n;j++) sm.addTag((*qr.second)[j]);
    int fresh=(int)sm.knownNames.size()-before;
    o.probes++; o.fresh+=fresh;
    if(q.size()==2) o.d2q++; else if(q.size()==3) o.d3q++;
    sm.addGram(q,n,fresh);
    sm.processed++;
    sm.yieldE=sm.processed==1?fresh:sm.yieldE*.82+fresh*.18;
    sm.depthSum+=q.size();
    if(n<K){
        o.closed++; sm.closedq++;
        if(q.size()==2) o.d2c++; else if(q.size()==3) o.d3c++;
        int k=sm.pruneClosed(q); o.pruned+=k;
        sm.noteFront(f0,1);
        if(fresh==0&&k==0) sm.redundant++;
        int eff=max(1,f0-sm.frontierSize());
        sm.pruneEff=sm.pruneSamples?sm.pruneEff*.88+eff*.12:eff;
        sm.pruneSamples++;
    }else{
        o.sat++; sm.satq++;
        if(q.size()==2) o.d2s++; else if(q.size()==3) o.d3s++;
        sm.noteFront(f0,1);
    }
    sm.discovery.push_back(sm.knownNames.size());
    sm.area+=sm.frontierSize();
    sm.turn++;
    while(sm.inferSweep()){}
    return {n,ob};
}

enum Rule { NONE, DISTINCT4, DISTINCT8, MAX20, MAX30, ENTROPY15, HYBRID };
static bool choose(Rule r,const Obs&o){
    if(o.count<K) return false;
    switch(r){
        case NONE: return false;
        case DISTINCT4: return o.distinct<=4;
        case DISTINCT8: return o.distinct<=8;
        case MAX20: return o.maxSupport>=20;
        case MAX30: return o.maxSupport>=30;
        case ENTROPY15: return o.entropy<=1.5;
        case HYBRID: return o.maxSupport>=24 || o.entropy<=1.8 || o.distinct<=6;
    }
    return false;
}

static AR runPolicy(World&w,Rule rule){
    Policy p=v1(); p.name="adaptive-period-recursive";
    Sim sm(w,p,"learnedprune"); AR out;
    auto root=probeQ(sm,".",out);
    if(root.first>=K){
        vector<pair<string,Obs>> parents;
        const string chars="abcdefghijklmnopqrstuvwxyz0123456789";
        for(char c:chars){
            string q=string(".")+c;
            auto z=probeQ(sm,q,out);
            if(z.first>=K && choose(rule,z.second)) parents.push_back({q,z.second});
        }
        for(auto &po:parents){
            out.selectedParents++;
            out.selected.push_back(po.first);
            for(char c:NEXT){
                string q=po.first+string(1,c);
                if(q.find("..")!=string::npos || sm.isCovered(q)) continue;
                probeQ(sm,q,out);
            }
        }
    }
    out.r=sm.run();
    return out;
}

static void printParentObservations(const World&w){
    cout<<fixed<<setprecision(3);
    const string chars="abcdefghijklmnopqrstuvwxyz0123456789";
    for(char c:chars){
        string q=string(".")+c;
        auto qr=w.query(q);
        if(qr.first<K) continue;
        Obs o=observe(w,q,qr.second,qr.first);
        cout<<"ADAPT_PARENT q="<<q<<" top="<<qr.first<<" distinct="<<o.distinct<<" max="<<o.maxSupport<<" entropy="<<o.entropy<<"\n";
    }
}

static void show(const string&name,const AR&x,const Result&b){
    cout<<"ADAPT policy="<<name
        <<" queries="<<x.r.queries<<" delta="<<(x.r.queries-b.queries)
        <<" probes="<<x.probes<<" probe_closed="<<x.closed<<" probe_sat="<<x.sat
        <<" selected_parents="<<x.selectedParents
        <<" d2q="<<x.d2q<<" d2c="<<x.d2c<<" d2s="<<x.d2s
        <<" d3q="<<x.d3q<<" d3c="<<x.d3c<<" d3s="<<x.d3s
        <<" probe_fresh="<<x.fresh<<" probe_pruned="<<x.pruned
        <<" complete="<<(x.r.complete?1:0)
        <<" selected=";
    if(x.selected.empty()) cout<<"-"; else for(size_t i=0;i<x.selected.size();i++){if(i)cout<<",";cout<<x.selected[i];}
    cout<<"\n";
}

int main(int argc,char**argv){
    if(argc<2){cerr<<"usage: period_adaptive_recursive_probe WORLD.tsv\n";return 2;}
    World w=loadWorld(argv[1]);
    Policy p=v1(); Sim base(w,p,"learnedprune"); Result br=base.run();
    if(!br.complete) return 3;
    cout<<"ADAPT_BASE queries="<<br.queries<<" complete=1\n";
    printParentObservations(w);
    vector<pair<string,Rule>> cases={
        {"all36_d2",NONE},
        {"distinct4_d3",DISTINCT4},
        {"distinct8_d3",DISTINCT8},
        {"max20_d3",MAX20},
        {"max30_d3",MAX30},
        {"entropy15_d3",ENTROPY15},
        {"hybrid_d3",HYBRID},
    };
    for(auto &c:cases){
        AR x=runPolicy(w,c.second); show(c.first,x,br); if(!x.r.complete)return 4;
    }
    return 0;
}
