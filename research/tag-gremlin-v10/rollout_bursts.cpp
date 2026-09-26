#define main tag_gremlin_native_main
#include "native_sim.cpp"
#undef main

#include <iomanip>

static int baselineChoice(Sim &sm){
    bool h=sm.harvestMode();
    return h?sm.choose(true):sm.chooseLearnedPrune();
}

static bool advanceForced(Sim &sm,const string &mode,int step){
    while(sm.inferSweep()){}
    sm.updateDebt();
    if(sm.active.empty()) return false;
    int id=-1; bool h=false;
    if(mode=="nodebt"){
        sm.debt=false;
        h=sm.harvestMode();
        id=h?sm.choose(true):sm.chooseLearnedPrune();
    }else if(mode=="harvest"){
        h=true; id=sm.choose(true);
    }else if(mode=="softmix"){
        id=sm.chooseSoftMix();
    }else if(mode=="softmix-harvesty"){
        sm.softTarget=.55; sm.softSlope=6.0;
        id=sm.chooseSoftMix();
    }else if(mode=="h3p1"){
        h=(step%4)!=3;
        id=h?sm.choose(true):sm.chooseLearnedPrune();
    }else{
        id=baselineChoice(sm); h=sm.harvestMode();
    }
    if(id<0) return false;
    sm.processQ(id,h); sm.turn++;
    return true;
}

static void evalSnapshot(Sim &sm,int idx,double target){
    Sim base=sm; base.repairIterators();
    Result br=base.run();
    if(!br.complete){
        cout<<"BURST_ERROR idx="<<idx<<" baseline_incomplete=1\n";
        return;
    }
    const vector<string> modes={"nodebt","harvest","softmix","softmix-harvesty","h3p1"};
    const vector<int> horizons={20,50,100};
    int best=br.queries; string bestMode="baseline"; int bestH=0;
    cout<<fixed<<setprecision(4)
        <<"BURST_SNAPSHOT idx="<<idx
        <<" target="<<target
        <<" req="<<sm.req
        <<" known="<<sm.knownNames.size()
        <<" frontier="<<sm.frontierSize()
        <<" fratio="<<sm.fratio()
        <<" debt="<<(sm.debt?1:0)
        <<" baseline_total="<<br.queries
        <<"\n";
    for(auto &mode:modes){
        for(int H:horizons){
            Sim c=sm; c.repairIterators();
            int done=0;
            for(;done<H && !c.active.empty();done++) if(!advanceForced(c,mode,done)) break;
            Result rr=c.run();
            int delta=rr.complete?rr.queries-br.queries:INT_MAX;
            cout<<"BURST_RESULT idx="<<idx
                <<" mode="<<mode
                <<" horizon="<<H
                <<" forced="<<done
                <<" total="<<rr.queries
                <<" delta="<<(rr.complete?delta:999999)
                <<" complete="<<(rr.complete?1:0)
                <<"\n";
            if(rr.complete && rr.queries<best){best=rr.queries;bestMode=mode;bestH=H;}
        }
    }
    cout<<"BURST_BEST idx="<<idx
        <<" baseline="<<br.queries
        <<" best="<<best
        <<" gain="<<(br.queries-best)
        <<" mode="<<bestMode
        <<" horizon="<<bestH
        <<"\n";
}

int main(int argc,char **argv){
    if(argc<2){cerr<<"usage: rollout_bursts WORLD.tsv\n";return 2;}
    World w=loadWorld(argv[1]);
    Policy p=v1(); p.name="learnedprune";
    Sim sm(w,p,"learnedprune");
    const vector<double> targets={.50,.90,.99};
    int ti=0;
    while(!sm.active.empty() && sm.req<1000000){
        while(sm.inferSweep()){}
        sm.updateDebt();
        if(sm.active.empty()) break;
        double frac=sm.knownNames.size()/(double)w.tags.size();
        while(ti<(int)targets.size() && frac>=targets[ti]){
            evalSnapshot(sm,ti,targets[ti]);
            ti++;
        }
        int id=baselineChoice(sm);
        if(id<0) break;
        bool h=sm.harvestMode();
        sm.processQ(id,h); sm.turn++;
    }
    Result fin=sm.run();
    cout<<"BURST_SUMMARY snapshots="<<ti<<" final_queries="<<fin.queries<<" complete="<<(fin.complete?1:0)<<"\n";
    return (fin.complete && ti==(int)targets.size())?0:3;
}
