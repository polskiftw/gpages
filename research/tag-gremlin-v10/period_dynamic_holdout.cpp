#define main tag_gremlin_dynamic_training_main
#include "period_dynamic_certificate.cpp"
#undef main

int main(int argc,char**argv){
    if(argc<2){cerr<<"usage: period_dynamic_holdout WORLD.tsv\n";return 2;}
    World w=loadWorld(argv[1]);
    Policy p=v1(); p.name="learnedprune";
    Sim base(w,p,"learnedprune");
    Result br=base.run();
    if(!br.complete){cerr<<"baseline incomplete\n";return 3;}

    const DynPolicy frozen{"cov8_s4",8,4};
    vector<string> cands=buildPeriodCandidates();
    auto [r,ds]=runDynamic(w,frozen,cands);
    cout<<"HOLDOUT_BASE queries="<<br.queries<<" closed="<<br.closedq<<" sat="<<br.satq
        <<" inferred="<<br.inferred<<" complete=1\n";
    cout<<"HOLDOUT policy="<<frozen.name
        <<" queries="<<r.queries
        <<" delta="<<(r.queries-br.queries)
        <<" probes="<<ds.probes
        <<" probe_closed="<<ds.closed
        <<" probe_sat="<<ds.sat
        <<" probe_fresh="<<ds.fresh
        <<" probe_pruned="<<ds.pruned
        <<" mean_selected_cov="<<(ds.probes?ds.cov_sum/(double)ds.probes:0.0)
        <<" mean_selected_support="<<(ds.probes?ds.support_sum/(double)ds.probes:0.0)
        <<" q99="<<r.q99
        <<" endgame="<<r.endgame
        <<" complete="<<(r.complete?1:0)<<"\n";
    return r.complete?0:4;
}
