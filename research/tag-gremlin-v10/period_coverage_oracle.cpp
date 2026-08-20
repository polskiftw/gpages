#define main tag_gremlin_native_main
#include "native_sim_period_only.cpp"
#undef main

#include <deque>
#include <iomanip>

struct CandCov {
    string q;
    int truth=0;
    vector<int> covers;
};

static bool legal(const string&s){ return s.size()>=2 && s[0]=='.' && s.find("..") == string::npos; }

int main(int argc,char**argv){
    if(argc<2){cerr<<"usage: period_coverage_oracle WORLD.tsv [MAXLEN]\n";return 2;}
    int maxLen=argc>=3?stoi(argv[2]):4;
    if(maxLen<2||maxLen>8)return 2;
    World w=loadWorld(argv[1]);

    // Enumerate the ordinary alphanumeric-root substring-search architecture.
    // Grammar is known for free: no consecutive periods.
    deque<string> todo;
    for(char c:ROOT) todo.push_back(string(1,c));
    vector<string> boundary;
    unordered_set<string> seen;
    seen.reserve(w.tags.size()*8);
    while(!todo.empty()){
        string q=move(todo.front()); todo.pop_front();
        if(!seen.insert(q).second)continue;
        auto qr=w.query(q);
        if(qr.first>=K){
            for(char c:NEXT){
                string x=q+string(1,c);
                if(x.find("..")!=string::npos)continue;
                todo.push_back(move(x));
            }
        }else boundary.push_back(move(q));
    }

    unordered_set<string> bset(boundary.begin(),boundary.end());
    vector<string> minimal;
    minimal.reserve(boundary.size());
    for(auto &q:boundary){
        bool smaller=false;
        for(int a=0;a<(int)q.size()&&!smaller;a++) for(int z=a+1;z<=(int)q.size();z++){
            if(a==0&&z==(int)q.size())continue;
            if(bset.count(q.substr(a,z-a))){smaller=true;break;}
        }
        if(!smaller)minimal.push_back(q);
    }

    // Candidate period-leading substrings need only be harvested from ordinary
    // mandatory proof nodes: any candidate occurring nowhere in that set has
    // zero proof-query value by construction.
    unordered_map<string,vector<int>> cov;
    cov.reserve(minimal.size()*3);
    for(int i=0;i<(int)minimal.size();i++){
        const string &m=minimal[i];
        unordered_set<string> local;
        for(int p=0;p<(int)m.size();p++) if(m[p]=='.'){
            for(int L=2;L<=maxLen&&p+L<=(int)m.size();L++){
                string q=m.substr(p,L);
                if(legal(q)) local.insert(move(q));
            }
        }
        for(auto &q:local) cov[q].push_back(i);
    }

    vector<CandCov> cc;
    cc.reserve(cov.size());
    int closedCandidates=0,satCandidates=0;
    for(auto &kv:cov){
        auto qr=w.query(kv.first);
        CandCov c{kv.first,qr.first,move(kv.second)};
        if(c.truth<K)closedCandidates++;else satCandidates++;
        cc.push_back(move(c));
    }

    vector<int> ord(cc.size());iota(ord.begin(),ord.end(),0);
    sort(ord.begin(),ord.end(),[&](int a,int b){
        if((cc[a].truth<K)!=(cc[b].truth<K))return cc[a].truth<K;
        if(cc[a].covers.size()!=cc[b].covers.size())return cc[a].covers.size()>cc[b].covers.size();
        if(cc[a].q.size()!=cc[b].q.size())return cc[a].q.size()<cc[b].q.size();
        return cc[a].q<cc[b].q;
    });

    cout<<"COVERAGE universe="<<minimal.size()<<" boundary="<<boundary.size()<<" candidates="<<cc.size()<<" closed_candidates="<<closedCandidates<<" sat_candidates="<<satCandidates<<" maxlen="<<maxLen<<"\n";
    int shown=0;
    for(int id:ord) if(cc[id].truth<K && cc[id].covers.size()>1 && shown<60){
        cout<<"COVER_TOP q="<<cc[id].q<<" truth="<<cc[id].truth<<" coverage="<<cc[id].covers.size()<<" single_net="<<((int)cc[id].covers.size()-1)<<"\n";
        shown++;
    }

    // Greedy maximum net proof saving.  Each selected external query costs one;
    // each newly covered ordinary minimal CLOSED node avoids one old proof query.
    vector<char> covered(minimal.size(),0),chosen(cc.size(),0);
    int coveredN=0,queries=0;
    while(true){
        int best=-1,bg=1; // need gain > cost=1 to improve total query count
        for(int i=0;i<(int)cc.size();i++){
            if(chosen[i]||cc[i].truth>=K)continue;
            int g=0;for(int x:cc[i].covers)if(!covered[x])g++;
            if(g>bg || (g==bg&&best>=0&&cc[i].q.size()<cc[best].q.size())){bg=g;best=i;}
        }
        if(best<0)break;
        chosen[best]=1;queries++;
        int newN=0;for(int x:cc[best].covers)if(!covered[x]){covered[x]=1;coveredN++;newN++;}
        cout<<"COVER_PICK rank="<<queries<<" q="<<cc[best].q<<" truth="<<cc[best].truth<<" new_coverage="<<newN<<" total_coverage="<<coveredN<<" cumulative_net="<<(coveredN-queries)<<"\n";
        if(queries>=5000){cerr<<"greedy runaway\n";return 5;}
    }
    cout<<"COVER_GREEDY selected="<<queries<<" covered="<<coveredN<<" predicted_net_saving="<<(coveredN-queries)<<" remaining_old_proofs="<<((int)minimal.size()-coveredN)<<"\n";

    // Evaluate several fixed architectures in the same exact proof universe.
    auto eval=[&](const string&name,const vector<string>&qs){
        vector<char> cv(minimal.size(),0);int cn=0,cost=0,cl=0,sa=0;
        unordered_set<string> uq;
        for(auto&q:qs)if(uq.insert(q).second){
            cost++;auto qr=w.query(q);if(qr.first<K)cl++;else sa++;
            if(qr.first>=K)continue;
            auto it=cov.find(q);if(it==cov.end())continue;
            for(int x:it->second)if(!cv[x]){cv[x]=1;cn++;}
        }
        cout<<"COVER_SET name="<<name<<" queries="<<cost<<" closed="<<cl<<" sat="<<sa<<" covered="<<cn<<" predicted_net_saving="<<(cn-cost)<<"\n";
    };
    vector<string> qvxz;for(char c:string("qvxz0123456789"))qvxz.push_back(string(".")+c);
    eval("qvxz_digits_d2",qvxz);
    vector<string> all2;for(char c:string("abcdefghijklmnopqrstuvwxyz0123456789"))all2.push_back(string(".")+c);
    eval("all36_d2",all2);

    // Direct depth-3 oracle candidates: unlike the old recursive experiment,
    // these are queried without paying for parent traversal.
    vector<string> all3;
    for(char a:string("abcdefghijklmnopqrstuvwxyz0123456789"))for(char b:NEXT){string q=".";q+=a;q+=b;if(legal(q))all3.push_back(move(q));}
    eval("all_depth3_direct",all3);

    return 0;
}
