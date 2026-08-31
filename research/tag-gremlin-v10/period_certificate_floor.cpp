#define main tag_gremlin_native_main
#include "native_sim_period_only.cpp"
#undef main

#include <algorithm>
#include <cmath>
#include <deque>
#include <iomanip>
#include <numeric>
#include <random>
#include <unordered_map>
#include <unordered_set>
#include <vector>

// Proof-only floor for the corrected architecture.
//
// Ordinary substring-prefix traversal has a set of minimal CLOSED boundary
// queries.  Each of those queries is a one-request proof obligation unless a
// different CLOSED external period-leading substring certificate proves several
// obligations at once.  We model the currently validated direct-certificate
// universe (.x and .xy) as unit-cost set-cover actions over those obligations.
//
// The exact integer minimum is a set-cover problem, so this program reports:
//   * prefix_only: old one-query-per-minimal-proof count;
//   * constructive_upper: a realizable omniscient greedy cover cost;
//   * packing_lower: a rigorous integer lower bound from mutually incompatible
//     proof obligations (no legal CLOSED direct certificate covers two of them);
//   * fractional_dual_lower: another rigorous lower bound from an explicitly
//     feasible dual weighting.  Multiple deterministic orders are tried, but
//     every reported dual solution is verified before use.
//
// These are structural proof costs, not claims about the total runtime query
// count: discovery/saturated traversal can overlap proof queries and is analyzed
// separately by the scheduler/oracle harnesses.

static bool legalPeriodCandidate(const string &q){
    if(q.size()<2 || q.size()>3 || q[0]!='.') return false;
    if(q.find("..")!=string::npos) return false;
    for(size_t i=1;i<q.size();++i){
        char c=q[i];
        if(!((c>='a'&&c<='z') || (c>='0'&&c<='9') || c=='.')) return false;
    }
    return true;
}

struct Cert {
    string q;
    int truth=0;
    vector<int> cover;
};

static vector<string> enumerateMinimalClosed(World &w){
    deque<string> todo;
    for(char c:ROOT) todo.push_back(string(1,c));
    vector<string> boundary;
    unordered_set<string> seen;
    seen.reserve(w.tags.size()*8);

    while(!todo.empty()){
        string q=move(todo.front()); todo.pop_front();
        if(!seen.insert(q).second) continue;
        auto qr=w.query(q);
        if(qr.first>=K){
            for(char c:NEXT){
                string x=q+string(1,c);
                if(x.find("..")!=string::npos) continue;
                todo.push_back(move(x));
            }
        }else boundary.push_back(move(q));
    }

    unordered_set<string> bset(boundary.begin(),boundary.end());
    vector<string> minimal;
    minimal.reserve(boundary.size());
    for(const string &q:boundary){
        bool smaller=false;
        for(int a=0;a<(int)q.size() && !smaller;a++){
            for(int z=a+1;z<=(int)q.size();z++){
                if(a==0 && z==(int)q.size()) continue;
                if(bset.count(q.substr(a,z-a))){ smaller=true; break; }
            }
        }
        if(!smaller) minimal.push_back(q);
    }
    return minimal;
}

static vector<Cert> buildClosedCerts(World &w,const vector<string>&minimal){
    unordered_map<string,vector<int>> covers;
    covers.reserve(minimal.size()*2);
    for(int i=0;i<(int)minimal.size();++i){
        const string &m=minimal[i];
        unordered_set<string> local;
        for(int p=0;p<(int)m.size();++p){
            if(m[p]!='.') continue;
            for(int len=2;len<=3 && p+len<=(int)m.size();++len){
                string q=m.substr(p,len);
                if(legalPeriodCandidate(q)) local.insert(move(q));
            }
        }
        for(const string &q:local) covers[q].push_back(i);
    }

    vector<Cert> out;
    out.reserve(covers.size());
    for(auto &kv:covers){
        auto qr=w.query(kv.first);
        if(qr.first>=K) continue;
        auto v=move(kv.second);
        sort(v.begin(),v.end());
        v.erase(unique(v.begin(),v.end()),v.end());
        out.push_back({kv.first,qr.first,move(v)});
    }
    sort(out.begin(),out.end(),[](const Cert&a,const Cert&b){return a.q<b.q;});
    return out;
}

static int greedyConstructive(const vector<Cert>&certs,int U,int &picked,int &coveredN){
    vector<char> covered(U,0),used(certs.size(),0);
    picked=coveredN=0;
    while(true){
        int best=-1,bg=1; // cost is one, so gain must exceed one.
        for(int i=0;i<(int)certs.size();++i){
            if(used[i]) continue;
            int g=0;
            for(int x:certs[i].cover) if(!covered[x]) ++g;
            if(g>bg || (g==bg && best>=0 && certs[i].q<certs[best].q)){
                best=i; bg=g;
            }
        }
        if(best<0) break;
        used[best]=1; ++picked;
        for(int x:certs[best].cover) if(!covered[x]){covered[x]=1;++coveredN;}
    }
    return picked + (U-coveredN);
}

static int packingLower(const vector<Cert>&certs,int U){
    // Two obligations conflict if some unit-cost CLOSED certificate covers both.
    // Any independent set in this graph requires at least one request per node.
    vector<unordered_set<int>> adj(U);
    for(const Cert &c:certs){
        const auto &v=c.cover;
        for(int i=0;i<(int)v.size();++i){
            for(int j=i+1;j<(int)v.size();++j){
                adj[v[i]].insert(v[j]);
                adj[v[j]].insert(v[i]);
            }
        }
    }

    vector<int> base(U); iota(base.begin(),base.end(),0);
    auto greedy=[&](vector<int> order){
        vector<char> blocked(U,0);
        int n=0;
        for(int x:order){
            if(blocked[x]) continue;
            ++n; blocked[x]=1;
            for(int y:adj[x]) blocked[y]=1;
        }
        return n;
    };

    sort(base.begin(),base.end(),[&](int a,int b){
        if(adj[a].size()!=adj[b].size()) return adj[a].size()<adj[b].size();
        return a<b;
    });
    int best=greedy(base);

    // Randomized tie/order restarts remain rigorous because every produced set
    // is independently valid; randomness only searches for a larger packing.
    mt19937_64 rng(0x7461676772656d6ULL ^ (uint64_t)U ^ ((uint64_t)certs.size()<<32));
    vector<int> ord(U);
    iota(ord.begin(),ord.end(),0);
    for(int rep=0;rep<128;++rep){
        shuffle(ord.begin(),ord.end(),rng);
        stable_sort(ord.begin(),ord.end(),[&](int a,int b){
            size_t da=adj[a].size(),db=adj[b].size();
            if(da/4!=db/4) return da<db;
            return false;
        });
        best=max(best,greedy(ord));
    }
    return best;
}

static double feasibleDual(const vector<Cert>&certs,int U,uint64_t seed){
    // Start with singleton constraints tight (y_i=1), then monotonically scale
    // violated certificate constraints down. Previously satisfied constraints
    // cannot become violated because weights only decrease.
    vector<double> y(U,1.0);
    vector<int> ord(certs.size()); iota(ord.begin(),ord.end(),0);
    mt19937_64 rng(seed);
    shuffle(ord.begin(),ord.end(),rng);
    stable_sort(ord.begin(),ord.end(),[&](int a,int b){
        // Large constraints first, randomized within equal cardinality.
        return certs[a].cover.size()>certs[b].cover.size();
    });

    for(int ci:ord){
        const auto &v=certs[ci].cover;
        double s=0; for(int x:v) s+=y[x];
        if(s>1.0+1e-15){
            double f=1.0/s;
            for(int x:v) y[x]*=f;
        }
    }

    // Verify the constructed dual before accepting it.
    for(double x:y) if(x<-1e-12 || x>1.0+1e-9) return -1;
    for(const Cert &c:certs){
        double s=0; for(int x:c.cover) s+=y[x];
        if(s>1.0+1e-8) return -1;
    }
    return accumulate(y.begin(),y.end(),0.0);
}

int main(int argc,char**argv){
    if(argc<2){cerr<<"usage: period_certificate_floor WORLD.tsv\n";return 2;}
    World w=loadWorld(argv[1]);
    vector<string> minimal=enumerateMinimalClosed(w);
    vector<Cert> certs=buildClosedCerts(w,minimal);
    const int U=(int)minimal.size();

    int maxCover=1, useful=0;
    long long coverMass=0;
    for(const Cert &c:certs){
        int n=(int)c.cover.size();
        maxCover=max(maxCover,n);
        coverMass+=n;
        if(n>1) ++useful;
    }

    int picked=0,covered=0;
    int constructive=greedyConstructive(certs,U,picked,covered);
    int packing=packingLower(certs,U);

    double bestDual=0;
    for(int rep=0;rep<96;++rep){
        double d=feasibleDual(certs,U,0xC0FFEE123400ULL+(uint64_t)rep*0x9E3779B97F4A7C15ULL);
        bestDual=max(bestDual,d);
    }
    int dualLB=(int)ceil(bestDual-1e-9);
    int simpleLB=(U+maxCover-1)/maxCover;
    int rigorousLB=max({simpleLB,packing,dualLB});

    cout<<fixed<<setprecision(6)
        <<"PERIOD_PROOF_FLOOR tags="<<w.tags.size()
        <<" prefix_only="<<U
        <<" closed_direct_candidates="<<certs.size()
        <<" useful_candidates="<<useful
        <<" candidate_cover_mass="<<coverMass
        <<" max_cover="<<maxCover
        <<" greedy_external_picks="<<picked
        <<" greedy_external_covered="<<covered
        <<" constructive_upper="<<constructive
        <<" constructive_saving="<<(U-constructive)
        <<" simple_lower="<<simpleLB
        <<" packing_lower="<<packing
        <<" fractional_dual_value="<<bestDual
        <<" fractional_dual_lower="<<dualLB
        <<" rigorous_lower="<<rigorousLB
        <<" unresolved_gap="<<(constructive-rigorousLB)
        <<"\n";

    vector<int> ord(certs.size()); iota(ord.begin(),ord.end(),0);
    sort(ord.begin(),ord.end(),[&](int a,int b){
        if(certs[a].cover.size()!=certs[b].cover.size())return certs[a].cover.size()>certs[b].cover.size();
        return certs[a].q<certs[b].q;
    });
    int shown=0;
    for(int i:ord){
        if(certs[i].cover.size()<=1 || shown>=32) break;
        cout<<"PERIOD_PROOF_TOP q="<<certs[i].q
            <<" truth="<<certs[i].truth
            <<" coverage="<<certs[i].cover.size()
            <<"\n";
        ++shown;
    }

    if(constructive>U || rigorousLB>constructive){
        cerr<<"invalid proof-floor accounting\n";
        return 4;
    }
    return 0;
}
