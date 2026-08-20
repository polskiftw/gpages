#define main tag_gremlin_native_main
#include "native_sim_period_only.cpp"
#undef main

struct PR { Result r; int probes=0,closed=0,sat=0,fresh=0; };

static int doProbe(Sim &sm,const string&q,int &cl,int &sa,int &fr){
    int f0=sm.frontierSize(),before=(int)sm.knownNames.size();
    auto qr=sm.w.query(q);int count=qr.first;sm.req++;
    if(qr.second)for(int j=0;j<count;j++)sm.addTag((*qr.second)[j]);
    int fresh=(int)sm.knownNames.size()-before;fr+=fresh;sm.addGram(q,count,fresh);sm.processed++;
    sm.yieldE=sm.processed==1?fresh:sm.yieldE*.82+fresh*.18;sm.depthSum+=q.size();
    if(count<K){cl++;sm.closedq++;int n=sm.pruneClosed(q);sm.noteFront(f0,1);if(fresh==0&&n==0)sm.redundant++;int eff=max(1,f0-sm.frontierSize());sm.pruneEff=sm.pruneSamples?sm.pruneEff*.88+eff*.12:eff;sm.pruneSamples++;}
    else{sa++;sm.satq++;sm.noteFront(f0,1);}sm.discovery.push_back(sm.knownNames.size());sm.area+=sm.frontierSize();sm.turn++;
    while(sm.inferSweep()){}return count;
}

static vector<char> chars(){vector<char>v;for(char c:NEXT)if(c!='.')v.push_back(c);return v;}

static PR runSide(World&w,int mode){
    // mode 0 root only, 1 right .x, 2 left x., 3 both right then left,
    // 4 both left then right.  Order is measured because CLOSED certificates
    // can alter later scheduler state even when the set of probes is identical.
    Policy p=v1();Sim sm(w,p,"learnedprune");int probes=0,cl=0,sa=0,fr=0;
    int root=doProbe(sm,".",cl,sa,fr);probes++;
    if(root>=K){
        auto cc=chars();
        auto right=[&](){for(char c:cc){string q="."+string(1,c);if(!sm.isCovered(q)){doProbe(sm,q,cl,sa,fr);probes++;}}};
        auto left=[&](){for(char c:cc){string q=string(1,c)+".";if(!sm.isCovered(q)){doProbe(sm,q,cl,sa,fr);probes++;}}};
        if(mode==1)right();else if(mode==2)left();else if(mode==3){right();left();}else if(mode==4){left();right();}
    }
    Result r=sm.run();return {r,probes,cl,sa,fr};
}

int main(int argc,char**argv){
    if(argc<2){cerr<<"usage: period_side_probe WORLD.tsv\n";return 2;}
    World w=loadWorld(argv[1]);Policy p=v1();Sim base(w,p,"learnedprune");Result br=base.run();if(!br.complete)return 3;
    cout<<"SIDE_BASE queries="<<br.queries<<" complete=1\n";
    vector<pair<string,int>> tests={{"root",0},{"right_dotx",1},{"left_xdot",2},{"both_right_first",3},{"both_left_first",4}};
    for(auto &[name,m]:tests){auto x=runSide(w,m);cout<<"SIDE policy="<<name<<" queries="<<x.r.queries<<" delta="<<(x.r.queries-br.queries)<<" probes="<<x.probes<<" probe_closed="<<x.closed<<" probe_sat="<<x.sat<<" probe_fresh="<<x.fresh<<" closed="<<x.r.closedq<<" sat="<<x.r.satq<<" complete="<<(x.r.complete?1:0)<<"\n";if(!x.r.complete)return 4;}
    return 0;
}
