#define main tag_gremlin_native_main
#include "native_sim_period_only.cpp"
#undef main

struct R { Result r; int probes=0,closed=0,sat=0,fresh=0; };

static int qp(Sim &sm,const string&q,int&cl,int&sa,int&fr){
 int f0=sm.frontierSize(),before=(int)sm.knownNames.size();auto qr=sm.w.query(q);int n=qr.first;sm.req++;
 if(qr.second)for(int j=0;j<n;j++)sm.addTag((*qr.second)[j]);int fresh=(int)sm.knownNames.size()-before;fr+=fresh;sm.addGram(q,n,fresh);sm.processed++;sm.yieldE=sm.processed==1?fresh:sm.yieldE*.82+fresh*.18;sm.depthSum+=q.size();
 if(n<K){cl++;sm.closedq++;int k=sm.pruneClosed(q);sm.noteFront(f0,1);if(fresh==0&&k==0)sm.redundant++;int eff=max(1,f0-sm.frontierSize());sm.pruneEff=sm.pruneSamples?sm.pruneEff*.88+eff*.12:eff;sm.pruneSamples++;}else{sa++;sm.satq++;sm.noteFront(f0,1);}sm.discovery.push_back(sm.knownNames.size());sm.area+=sm.frontierSize();sm.turn++;while(sm.inferSweep()){}return n;
}

static R runSet(World&w,const string &cs){Policy p=v1();Sim sm(w,p,"learnedprune");int pr=0,cl=0,sa=0,fr=0;int root=qp(sm,".",cl,sa,fr);pr++;if(root>=K)for(char c:cs){qp(sm,string(".")+c,cl,sa,fr);pr++;}Result r=sm.run();return{r,pr,cl,sa,fr};}

int main(int argc,char**argv){if(argc<2)return 2;World w=loadWorld(argv[1]);Policy p=v1();Sim b(w,p,"learnedprune");Result br=b.run();if(!br.complete)return 3;cout<<"SPARSE_BASE queries="<<br.queries<<" complete=1\n";
 vector<pair<string,string>> sets={
  {"root",""},
  {"digits","0123456789"},
  {"qxz_digits","qxz0123456789"},
  {"qvxz_digits","qvxz0123456789"},
  {"digits_qvxz","0123456789qvxz"},
  {"all36","abcdefghijklmnopqrstuvwxyz0123456789"}
 };
 for(auto &[name,s]:sets){auto x=runSet(w,s);cout<<"SPARSE policy="<<name<<" n="<<s.size()<<" queries="<<x.r.queries<<" delta="<<(x.r.queries-br.queries)<<" probes="<<x.probes<<" probe_closed="<<x.closed<<" probe_sat="<<x.sat<<" probe_fresh="<<x.fresh<<" closed="<<x.r.closedq<<" sat="<<x.r.satq<<" complete="<<(x.r.complete?1:0)<<"\n";if(!x.r.complete)return 4;}
 return 0;}
