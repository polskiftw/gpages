#define main tag_gremlin_native_main
#include "native_sim_period_only.cpp"
#undef main

#include <array>
#include <iomanip>
#include <optional>

struct ProbeObs {
    string q;
    int count=0;
    int fresh=0;
    int closed=0;
    int sat=0;
    vector<int> ids;
};

static ProbeObs processExternalProbe(Sim &sm,const string &q){
    ProbeObs o; o.q=q;
    int f0=sm.frontierSize();
    int before=(int)sm.knownNames.size();
    auto qr=sm.w.query(q);
    o.count=qr.first;
    sm.req++;
    if(qr.second){
        for(int j=0;j<o.count;j++){
            int id=(*qr.second)[j];
            o.ids.push_back(id);
            sm.addTag(id);
        }
    }
    o.fresh=(int)sm.knownNames.size()-before;
    sm.addGram(q,o.count,o.fresh);
    sm.processed++;
    sm.yieldE=sm.processed==1?o.fresh:sm.yieldE*.82+o.fresh*.18;
    sm.depthSum+=q.size();
    if(o.count<K){
        o.closed=1; sm.closedq++;
        int n=sm.pruneClosed(q);
        sm.noteFront(f0,1);
        if(o.fresh==0&&n==0) sm.redundant++;
        int eff=max(1,f0-sm.frontierSize());
        sm.pruneEff=sm.pruneSamples?sm.pruneEff*.88+eff*.12:eff;
        sm.pruneSamples++;
    }else{
        o.sat=1; sm.satq++;
        sm.noteFront(f0,1);
    }
    sm.discovery.push_back(sm.knownNames.size());
    sm.area+=sm.frontierSize();
    sm.turn++;
    return o;
}

static Result finishFrom(Sim sm,const vector<string> &probes,vector<ProbeObs> *obs=nullptr){
    sm.repairIterators();
    for(const string &q:probes){
        if(sm.isCovered(q)) continue;
        ProbeObs o=processExternalProbe(sm,q);
        if(obs) obs->push_back(o);
        while(sm.inferSweep()){}
    }
    return sm.run();
}

static vector<string> allChildren(){
    vector<string> v;
    for(char c:NEXT) v.push_back(string(".")+c);
    return v;
}

static string printableChild(const string &q){
    if(q=="..") return "dot";
    return string(1,q[1]);
}

int main(int argc,char **argv){
    if(argc<2){cerr<<"usage: period_child_ablation WORLD.tsv\n";return 2;}
    World w=loadWorld(argv[1]);
    Policy p=v1(); p.name="learnedprune";

    Sim base(w,p,"learnedprune");
    Result br=base.run();
    if(!br.complete){cerr<<"baseline incomplete\n";return 3;}

    Sim root(w,p,"learnedprune");
    ProbeObs rootObs=processExternalProbe(root,".");
    while(root.inferSweep()){}
    if(rootObs.count<K){
        Result rr=finishFrom(root,{});
        cout<<"ABLATION_SUMMARY base="<<br.queries<<" root="<<rr.queries
            <<" all="<<rr.queries<<" root_count="<<rootObs.count
            <<" root_fresh="<<rootObs.fresh<<" children=0 complete="<<(rr.complete?1:0)<<"\n";
        return rr.complete?0:4;
    }

    vector<string> children=allChildren();

    // Runtime-observable predictor from the 40 tags returned by the root '.' query:
    // count which characters immediately follow a period in those visible tags.
    array<int,256> after{};
    for(int id:rootObs.ids){
        const string &t=w.tags[id];
        for(size_t i=0;i+1<t.size();i++) if(t[i]=='.') after[(unsigned char)t[i+1]]++;
    }

    Result rootR=finishFrom(root,{});
    vector<ProbeObs> fullObs;
    Result allR=finishFrom(root,children,&fullObs);
    if(!rootR.complete||!allR.complete){cerr<<"root/all incomplete\n";return 4;}

    vector<string> sampleSeen,sample2,lettersOnly,digitsOnly;
    for(const string &q:children){
        char c=q[1];
        if(after[(unsigned char)c]>=1) sampleSeen.push_back(q);
        if(after[(unsigned char)c]>=2) sample2.push_back(q);
        if(c>='a'&&c<='z') lettersOnly.push_back(q);
        if(c>='0'&&c<='9') digitsOnly.push_back(q);
    }
    Result seenR=finishFrom(root,sampleSeen);
    Result sample2R=finishFrom(root,sample2);
    Result lettersR=finishFrom(root,lettersOnly);
    Result digitsR=finishFrom(root,digitsOnly);
    if(!seenR.complete||!sample2R.complete||!lettersR.complete||!digitsR.complete){
        cerr<<"candidate subset incomplete\n";return 5;
    }

    cout<<"ABLATION_SUMMARY base="<<br.queries
        <<" root="<<rootR.queries
        <<" all="<<allR.queries
        <<" all_delta="<<(allR.queries-br.queries)
        <<" root_count="<<rootObs.count
        <<" root_fresh="<<rootObs.fresh
        <<" children="<<children.size()
        <<" complete=1\n";
    cout<<"ABLATION_POLICY name=sample_seen n="<<sampleSeen.size()<<" queries="<<seenR.queries
        <<" delta_vs_base="<<(seenR.queries-br.queries)<<" regret_vs_all="<<(seenR.queries-allR.queries)<<"\n";
    cout<<"ABLATION_POLICY name=sample_ge2 n="<<sample2.size()<<" queries="<<sample2R.queries
        <<" delta_vs_base="<<(sample2R.queries-br.queries)<<" regret_vs_all="<<(sample2R.queries-allR.queries)<<"\n";
    cout<<"ABLATION_POLICY name=letters_only n="<<lettersOnly.size()<<" queries="<<lettersR.queries
        <<" delta_vs_base="<<(lettersR.queries-br.queries)<<" regret_vs_all="<<(lettersR.queries-allR.queries)<<"\n";
    cout<<"ABLATION_POLICY name=digits_only n="<<digitsOnly.size()<<" queries="<<digitsR.queries
        <<" delta_vs_base="<<(digitsR.queries-br.queries)<<" regret_vs_all="<<(digitsR.queries-allR.queries)<<"\n";

    unordered_map<string,ProbeObs> obsByQ;
    for(auto &o:fullObs) obsByQ[o.q]=o;

    // Leave-one-out marginal value around the successful all-children policy.
    for(const string &drop:children){
        vector<string> keep; keep.reserve(children.size()-1);
        for(const string &q:children) if(q!=drop) keep.push_back(q);
        Result r=finishFrom(root,keep);
        if(!r.complete){cerr<<"loo incomplete "<<drop<<"\n";return 6;}
        auto it=obsByQ.find(drop);
        ProbeObs o;
        if(it!=obsByQ.end()) o=it->second;
        char c=drop[1];
        cout<<"ABLATION_CHILD child="<<printableChild(drop)
            <<" sample_after="<<after[(unsigned char)c]
            <<" full_count="<<o.count
            <<" full_fresh="<<o.fresh
            <<" full_closed="<<o.closed
            <<" full_sat="<<o.sat
            <<" loo_queries="<<r.queries
            <<" loo_penalty="<<(r.queries-allR.queries)
            <<"\n";
    }
    return 0;
}
