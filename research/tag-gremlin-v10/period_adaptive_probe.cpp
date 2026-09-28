#define main tag_gremlin_native_main
#include "native_sim_period_only.cpp"
#undef main

#include <array>
#include <algorithm>

struct Obs { int count=0,fresh=0,closed=0,sat=0; vector<int> ids; };

static Obs probe(Sim &sm,const string &q){
    Obs o; int f0=sm.frontierSize(), before=(int)sm.knownNames.size();
    auto qr=sm.w.query(q); o.count=qr.first; sm.req++;
    if(qr.second) for(int j=0;j<o.count;j++){int id=(*qr.second)[j];o.ids.push_back(id);sm.addTag(id);}
    o.fresh=(int)sm.knownNames.size()-before;
    sm.addGram(q,o.count,o.fresh); sm.processed++;
    sm.yieldE=sm.processed==1?o.fresh:sm.yieldE*.82+o.fresh*.18;
    sm.depthSum+=q.size();
    if(o.count<K){
        o.closed=1; sm.closedq++; int n=sm.pruneClosed(q); sm.noteFront(f0,1);
        if(o.fresh==0&&n==0) sm.redundant++;
        int eff=max(1,f0-sm.frontierSize()); sm.pruneEff=sm.pruneSamples?sm.pruneEff*.88+eff*.12:eff; sm.pruneSamples++;
    }else{o.sat=1;sm.satq++;sm.noteFront(f0,1);}
    sm.discovery.push_back(sm.knownNames.size());sm.area+=sm.frontierSize();sm.turn++;
    while(sm.inferSweep()){}
    return o;
}

static vector<char> legalChildren(){
    vector<char> v; for(char c:NEXT) if(c!='.') v.push_back(c); return v;
}

struct PolicySpec { string name; int topN=-1; int minSample=1; int expandSat=-1; };

struct AR { Result r; int probes=0,pilot=0,pilotSat=0,pilotClosed=0; };

static AR runAdaptive(World &w,const PolicySpec &spec){
    Policy p=v1(); Sim sm(w,p,"learnedprune");
    Obs root=probe(sm,"."); int probes=1;
    if(root.count<K){ return {sm.run(),probes,0,0,0}; }

    array<int,256> after{};
    for(int id:root.ids){
        const string &t=w.tags[id];
        for(size_t i=0;i+1<t.size();i++) if(t[i]=='.') after[(unsigned char)t[i+1]]++;
    }
    vector<char> ranked=legalChildren();
    stable_sort(ranked.begin(),ranked.end(),[&](char a,char b){
        if(after[(unsigned char)a]!=after[(unsigned char)b]) return after[(unsigned char)a]>after[(unsigned char)b];
        return a<b;
    });
    vector<char> pilot;
    for(char c:ranked){
        if(after[(unsigned char)c] < spec.minSample) continue;
        pilot.push_back(c);
        if(spec.topN>=0 && (int)pilot.size()>=spec.topN) break;
    }
    unordered_set<char> done;
    int sat=0,closed=0;
    for(char c:pilot){
        Obs o=probe(sm,string(".")+c);probes++;done.insert(c);sat+=o.sat;closed+=o.closed;
    }
    if(spec.expandSat>=0 && sat>=spec.expandSat){
        for(char c:legalChildren()) if(!done.count(c)){probe(sm,string(".")+c);probes++;}
    }
    Result r=sm.run();
    return {r,probes,(int)pilot.size(),sat,closed};
}

int main(int argc,char **argv){
    if(argc<2){cerr<<"usage: period_adaptive_probe WORLD.tsv\n";return 2;}
    World w=loadWorld(argv[1]); Policy p=v1(); Sim b(w,p,"learnedprune"); Result br=b.run();
    if(!br.complete){cerr<<"baseline incomplete\n";return 3;}
    cout<<"ADAPT_BASE queries="<<br.queries<<" complete=1\n";
    vector<PolicySpec> specs={
        {"root_only",0,99,-1},
        {"sample_seen",-1,1,-1},
        {"sample_ge2",-1,2,-1},
        {"top8",8,1,-1},
        {"top8_expand1",8,1,1},
        {"top8_expand2",8,1,2},
        {"top12_expand2",12,1,2},
        {"all36",36,0,-1},
    };
    for(auto &s:specs){
        AR a=runAdaptive(w,s);
        cout<<"ADAPT policy="<<s.name
            <<" queries="<<a.r.queries
            <<" delta="<<(a.r.queries-br.queries)
            <<" probes="<<a.probes
            <<" pilot="<<a.pilot
            <<" pilot_sat="<<a.pilotSat
            <<" pilot_closed="<<a.pilotClosed
            <<" closed="<<a.r.closedq
            <<" sat="<<a.r.satq
            <<" complete="<<(a.r.complete?1:0)<<"\n";
        if(!a.r.complete)return 4;
    }
    return 0;
}
