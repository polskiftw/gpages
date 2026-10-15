#include "playable_mesh.hpp"
#include "playable_room.hpp"

#include <SDL3/SDL.h>
#include <SDL3/SDL_opengl.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <fstream>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
using labyrinth::demo::Mesh;
using labyrinth::demo::Pose;
using labyrinth::demo::Room;

constexpr float Pi=3.14159265358979323846f;
struct V3 {
    float x,y,z;
};
V3 sub(V3 a,V3 b) { return {a.x-b.x,a.y-b.y,a.z-b.z}; }
float dot(V3 a,V3 b) { return a.x*b.x+a.y*b.y+a.z*b.z; }
V3 cross(V3 a,V3 b) {
    return {a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x};
}
V3 normalized(V3 v) {
    float l=std::sqrt(std::max(dot(v,v),1e-15f));
    return {v.x/l,v.y/l,v.z/l};
}

struct Mat4 {
    std::array<float,16> d{};
    float& at(int row,int col) { return d[static_cast<std::size_t>(col*4+row)]; }
    float at(int row,int col) const { return d[static_cast<std::size_t>(col*4+row)]; }
};
Mat4 operator*(const Mat4& a,const Mat4& b) {
    Mat4 c;
    for(int col=0;col<4;++col) for(int row=0;row<4;++row)
        for(int k=0;k<4;++k) c.at(row,col)+=a.at(row,k)*b.at(k,col);
    return c;
}
Mat4 perspective(float aspect) {
    const float f=1.0f/std::tan(75.0f*Pi/360.0f);
    constexpr float near=.025f,far=48.0f;
    Mat4 m;
    m.at(0,0)=f/aspect; m.at(1,1)=f;
    m.at(2,2)=(far+near)/(near-far);
    m.at(2,3)=(2.0f*far*near)/(near-far);
    m.at(3,2)=-1.0f;
    return m;
}
Mat4 look(V3 eye,float yaw,float pitch) {
    const V3 f=normalized({std::cos(yaw)*std::cos(pitch),
                           std::sin(yaw)*std::cos(pitch),std::sin(pitch)});
    const V3 r=normalized(cross(f,{0,0,1}));
    const V3 u=cross(r,f);
    Mat4 m;
    m.at(0,0)=r.x;m.at(0,1)=r.y;m.at(0,2)=r.z;m.at(0,3)=-dot(r,eye);
    m.at(1,0)=u.x;m.at(1,1)=u.y;m.at(1,2)=u.z;m.at(1,3)=-dot(u,eye);
    m.at(2,0)=-f.x;m.at(2,1)=-f.y;m.at(2,2)=-f.z;m.at(2,3)=dot(f,eye);
    m.at(3,3)=1;
    return m;
}

// This viewer targets native desktop OpenGL 3.3 Core. Resolving the modern
// entrypoints with SDL_GL_GetProcAddress avoids GLEW/GLAD dependencies.
template<typename T> T gl_proc(const char* name) {
    const SDL_FunctionPointer fn=SDL_GL_GetProcAddress(name);
    if (!fn) throw std::runtime_error(std::string("OpenGL function missing: ")+name);
    return reinterpret_cast<T>(fn);
}
using CreateShader=GLuint (*)(GLenum);
using ShaderSource=void (*)(GLuint,GLsizei,const GLchar* const*,const GLint*);
using CompileShader=void (*)(GLuint);
using GetShaderiv=void (*)(GLuint,GLenum,GLint*);
using GetShaderInfoLog=void (*)(GLuint,GLsizei,GLsizei*,GLchar*);
using DeleteShader=void (*)(GLuint);
using CreateProgram=GLuint (*)();
using AttachShader=void (*)(GLuint,GLuint);
using LinkProgram=void (*)(GLuint);
using GetProgramiv=void (*)(GLuint,GLenum,GLint*);
using GetProgramInfoLog=void (*)(GLuint,GLsizei,GLsizei*,GLchar*);
using DeleteProgram=void (*)(GLuint);
using UseProgram=void (*)(GLuint);
using GetUniformLocation=GLint (*)(GLuint,const GLchar*);
using UniformMatrix4fv=void (*)(GLint,GLsizei,GLboolean,const GLfloat*);
using GenVertexArrays=void (*)(GLsizei,GLuint*);
using BindVertexArray=void (*)(GLuint);
using DeleteVertexArrays=void (*)(GLsizei,const GLuint*);
using GenBuffers=void (*)(GLsizei,GLuint*);
using BindBuffer=void (*)(GLenum,GLuint);
using BufferData=void (*)(GLenum,std::ptrdiff_t,const void*,GLenum);
using DeleteBuffers=void (*)(GLsizei,const GLuint*);
using VertexAttribPointer=void (*)(GLuint,GLint,GLenum,GLboolean,GLsizei,const void*);
using EnableVertexAttribArray=void (*)(GLuint);

#ifndef GL_VERTEX_SHADER
#define GL_VERTEX_SHADER 0x8B31
#define GL_FRAGMENT_SHADER 0x8B30
#define GL_COMPILE_STATUS 0x8B81
#define GL_LINK_STATUS 0x8B82
#define GL_ARRAY_BUFFER 0x8892
#define GL_STATIC_DRAW 0x88E4
#endif

const char* VertexShader=R"glsl(
#version 330 core
layout(location=0) in vec3 a_pos;
layout(location=1) in vec3 a_normal;
layout(location=2) in vec2 a_uv;
layout(location=3) in float a_material;
uniform mat4 u_mvp;
out vec3 v_normal;
out vec2 v_uv;
flat out int v_material;
out float v_distance;
void main() {
    vec4 clip=u_mvp*vec4(a_pos,1.0);
    gl_Position=clip;
    v_normal=a_normal;
    v_uv=a_uv;
    v_material=int(a_material+0.5);
    v_distance=clip.w;
}
)glsl";

const char* FragmentShader=R"glsl(
#version 330 core
in vec3 v_normal;
in vec2 v_uv;
flat in int v_material;
in float v_distance;
out vec4 color;
void main() {
    vec3 base;
    if (v_material==0) {
        // Half-brick offsets create 1995-style red brickwork without any
        // texture downloads, files, shaders from elsewhere, or VRAM textures.
        float row=floor(v_uv.y*7.0);
        vec2 p=fract(vec2(v_uv.x*3.5+mod(row,2.0)*0.5,v_uv.y*7.0));
        bool mortar=p.x<0.045||p.y<0.065;
        float noise=mod(floor(v_uv.x*3.5+mod(row,2.0)*0.5)*23.0+row*11.0,7.0)/7.0;
        base=mortar ? vec3(0.22,0.19,0.17)
                    : vec3(0.51,0.165,0.115)*(0.86+noise*0.27);
    } else if (v_material==1) {
        float tile=mod(floor(v_uv.x*2.0)+floor(v_uv.y*2.0),2.0);
        base=mix(vec3(0.40,0.38,0.34),vec3(0.49,0.47,0.42),tile);
    } else if (v_material==2) {
        base=vec3(0.31,0.30,0.28);
    } else if (v_material==3) {
        base=vec3(0.56,0.51,0.39);
    } else if (v_material==4) {
        base=vec3(0.32,0.31,0.29);
    } else {
        base=vec3(0.79,0.74,0.38);
    }
    vec3 light=normalize(vec3(-0.38,0.55,0.74));
    float diffuse=abs(dot(normalize(v_normal),light));
    base*=(0.66+0.34*diffuse);
    float fog=clamp((v_distance-5.0)/24.0,0.0,0.72);
    color=vec4(mix(base,vec3(0.065,0.06,0.075),fog),1.0);
}
)glsl";

class Renderer {
public:
    Renderer()
        : create_shader(gl_proc<CreateShader>("glCreateShader")),
          shader_source(gl_proc<ShaderSource>("glShaderSource")),
          compile_shader(gl_proc<CompileShader>("glCompileShader")),
          get_shaderiv(gl_proc<GetShaderiv>("glGetShaderiv")),
          get_shader_log(gl_proc<GetShaderInfoLog>("glGetShaderInfoLog")),
          delete_shader(gl_proc<DeleteShader>("glDeleteShader")),
          create_program(gl_proc<CreateProgram>("glCreateProgram")),
          attach_shader(gl_proc<AttachShader>("glAttachShader")),
          link_program(gl_proc<LinkProgram>("glLinkProgram")),
          get_programiv(gl_proc<GetProgramiv>("glGetProgramiv")),
          get_program_log(gl_proc<GetProgramInfoLog>("glGetProgramInfoLog")),
          delete_program(gl_proc<DeleteProgram>("glDeleteProgram")),
          use_program(gl_proc<UseProgram>("glUseProgram")),
          get_uniform(gl_proc<GetUniformLocation>("glGetUniformLocation")),
          set_matrix(gl_proc<UniformMatrix4fv>("glUniformMatrix4fv")),
          gen_vao(gl_proc<GenVertexArrays>("glGenVertexArrays")),
          bind_vao(gl_proc<BindVertexArray>("glBindVertexArray")),
          delete_vao(gl_proc<DeleteVertexArrays>("glDeleteVertexArrays")),
          gen_buffer(gl_proc<GenBuffers>("glGenBuffers")),
          bind_buffer(gl_proc<BindBuffer>("glBindBuffer")),
          buffer_data(gl_proc<BufferData>("glBufferData")),
          delete_buffer(gl_proc<DeleteBuffers>("glDeleteBuffers")),
          attrib_pointer(gl_proc<VertexAttribPointer>("glVertexAttribPointer")),
          enable_attrib(gl_proc<EnableVertexAttribArray>("glEnableVertexAttribArray")) {
        const GLuint vs=compile(GL_VERTEX_SHADER,VertexShader);
        GLuint fs=0;
        try {
            fs=compile(GL_FRAGMENT_SHADER,FragmentShader);
            program=create_program();
            attach_shader(program,vs);attach_shader(program,fs);
            link_program(program);
            GLint ok=0;get_programiv(program,GL_LINK_STATUS,&ok);
            if (!ok) {
                char log[4096]={};
                get_program_log(program,sizeof(log),nullptr,log);
                throw std::runtime_error(std::string("GL shader link: ")+log);
            }
            mvp_location=get_uniform(program,"u_mvp");
            if(mvp_location<0) throw std::runtime_error("shader did not expose u_mvp");
            gen_vao(1,&vao);gen_buffer(1,&vbo);
            bind_vao(vao);bind_buffer(GL_ARRAY_BUFFER,vbo);
            attrib_pointer(0,3,GL_FLOAT,GL_FALSE,sizeof(labyrinth::demo::Vertex),
                           reinterpret_cast<const void*>(offsetof(labyrinth::demo::Vertex,x)));
            attrib_pointer(1,3,GL_FLOAT,GL_FALSE,sizeof(labyrinth::demo::Vertex),
                           reinterpret_cast<const void*>(offsetof(labyrinth::demo::Vertex,nx)));
            attrib_pointer(2,2,GL_FLOAT,GL_FALSE,sizeof(labyrinth::demo::Vertex),
                           reinterpret_cast<const void*>(offsetof(labyrinth::demo::Vertex,u)));
            attrib_pointer(3,1,GL_FLOAT,GL_FALSE,sizeof(labyrinth::demo::Vertex),
                           reinterpret_cast<const void*>(offsetof(labyrinth::demo::Vertex,material)));
            for(GLuint i=0;i<4;++i)enable_attrib(i);
            bind_vao(0);
            glEnable(GL_DEPTH_TEST);
            glClearColor(.065f,.06f,.075f,1.0f);
        } catch (...) {
            delete_shader(vs);
            if(fs)delete_shader(fs);
            throw;
        }
        delete_shader(vs);delete_shader(fs);
    }

    ~Renderer() {
        if(vbo)delete_buffer(1,&vbo);
        if(vao)delete_vao(1,&vao);
        if(program)delete_program(program);
    }

    void upload(const Mesh& m) {
        count=static_cast<GLsizei>(m.vertices().size());
        bind_vao(vao);bind_buffer(GL_ARRAY_BUFFER,vbo);
        buffer_data(GL_ARRAY_BUFFER,static_cast<std::ptrdiff_t>(
                    m.vertices().size()*sizeof(labyrinth::demo::Vertex)),
                    m.vertices().data(),GL_STATIC_DRAW);
        bind_vao(0);
    }

    void draw(int w,int h,Pose p,float yaw,float pitch) {
        w=std::max(w,1);h=std::max(h,1);
        glViewport(0,0,w,h);
        glClear(GL_COLOR_BUFFER_BIT|GL_DEPTH_BUFFER_BIT);
        Mat4 projection=perspective(static_cast<float>(w)/static_cast<float>(h));
        Mat4 view=look({static_cast<float>(p.x),static_cast<float>(p.y),
                        static_cast<float>(p.feet)+.55f},yaw,pitch);
        const Mat4 mvp=projection*view;
        use_program(program);
        set_matrix(mvp_location,1,GL_FALSE,mvp.d.data());
        bind_vao(vao);
        glDrawArrays(GL_TRIANGLES,0,count);
        bind_vao(0);
    }

private:
    GLuint compile(GLenum kind,const char* source) {
        const GLuint id=create_shader(kind);
        const GLchar* sources[]={source};
        shader_source(id,1,sources,nullptr);
        compile_shader(id);
        GLint ok=0;get_shaderiv(id,GL_COMPILE_STATUS,&ok);
        if(!ok) {
            char log[4096]={};
            get_shader_log(id,sizeof(log),nullptr,log);
            delete_shader(id);
            throw std::runtime_error(std::string("GL shader compile: ")+log);
        }
        return id;
    }
    CreateShader create_shader;
    ShaderSource shader_source;
    CompileShader compile_shader;
    GetShaderiv get_shaderiv;
    GetShaderInfoLog get_shader_log;
    DeleteShader delete_shader;
    CreateProgram create_program;
    AttachShader attach_shader;
    LinkProgram link_program;
    GetProgramiv get_programiv;
    GetProgramInfoLog get_program_log;
    DeleteProgram delete_program;
    UseProgram use_program;
    GetUniformLocation get_uniform;
    UniformMatrix4fv set_matrix;
    GenVertexArrays gen_vao;
    BindVertexArray bind_vao;
    DeleteVertexArrays delete_vao;
    GenBuffers gen_buffer;
    BindBuffer bind_buffer;
    BufferData buffer_data;
    DeleteBuffers delete_buffer;
    VertexAttribPointer attrib_pointer;
    EnableVertexAttribArray enable_attrib;
    GLuint program=0,vao=0,vbo=0;
    GLsizei count=0;
    GLint mvp_location=-1;
};

bool screenshot_ppm(const char* filename,int w,int h) {
    if(w<=0||h<=0||w>4096||h>4096) return false;
    glPixelStorei(GL_PACK_ALIGNMENT,1);
    std::vector<unsigned char> pixel(static_cast<std::size_t>(w)*h*3);
    glReadPixels(0,0,w,h,GL_RGB,GL_UNSIGNED_BYTE,pixel.data());
    if(glGetError()!=GL_NO_ERROR) return false;
    unsigned char minimum=255,maximum=0;
    for (auto c:pixel) {
        minimum=std::min(minimum,c);
        maximum=std::max(maximum,c);
    }
    if(maximum-minimum<12) return false; // Smoke screen must not be blank.
    std::ofstream out(filename,std::ios::binary);
    if(!out)return false;
    out<<"P6\n"<<w<<' '<<h<<"\n255\n";
    const std::size_t stride=static_cast<std::size_t>(w)*3;
    for(int row=h-1;row>=0;--row) {
        const char* data=reinterpret_cast<const char*>(
            pixel.data()+static_cast<std::size_t>(row)*stride);
        out.write(data,static_cast<std::streamsize>(stride));
    }
    return static_cast<bool>(out);
}

int run(int argc,char** argv) {
    const bool smoke=(argc>=3 && argc<=5 && std::string(argv[1])=="--smoke");
    if(argc!=1 && !smoke) {
        std::cerr<<"Usage: maze-playable [--smoke screenshot.ppm [1|2|3|4 [stairs|under]]]\n";
        return 2;
    }
    int smoke_mode=0;
    bool smoke_under=false;
    if(smoke) {
        if(argc>=4) {
            const std::string mode=argv[3];
            if(mode.size()!=1 || mode[0]<'1' || mode[0]>'4')
                throw std::invalid_argument("screenshot mode must be a digit 1..4");
            smoke_mode=mode[0]-'1';
        }
        if(argc==5) {
            const std::string view=argv[4];
            if(view!="stairs" && view!="under")
                throw std::invalid_argument("screenshot view must be stairs or under");
            smoke_under=(view=="under");
        }
    }
    if(!SDL_Init(SDL_INIT_VIDEO))
        throw std::runtime_error(std::string("SDL_Init: ")+SDL_GetError());
    SDL_Window* window=nullptr;
    SDL_GLContext context=nullptr;
    std::unique_ptr<Renderer> renderer;
    int result=1;
    try {
        if(!SDL_GL_SetAttribute(SDL_GL_CONTEXT_MAJOR_VERSION,3) ||
           !SDL_GL_SetAttribute(SDL_GL_CONTEXT_MINOR_VERSION,3) ||
           !SDL_GL_SetAttribute(SDL_GL_CONTEXT_PROFILE_MASK,SDL_GL_CONTEXT_PROFILE_CORE) ||
           !SDL_GL_SetAttribute(SDL_GL_DOUBLEBUFFER,1) ||
           !SDL_GL_SetAttribute(SDL_GL_DEPTH_SIZE,24))
            throw std::runtime_error(std::string("SDL_GL_SetAttribute: ")+SDL_GetError());
        window=SDL_CreateWindow("3D Maze Stair Laboratory",1280,720,
                              SDL_WINDOW_OPENGL|SDL_WINDOW_RESIZABLE|
                              (smoke?SDL_WINDOW_HIDDEN:0));
        if(!window)throw std::runtime_error(std::string("SDL_CreateWindow: ")+SDL_GetError());
        context=SDL_GL_CreateContext(window);
        if(!context)throw std::runtime_error(std::string("SDL_GL_CreateContext: ")+SDL_GetError());
        if(!SDL_GL_MakeCurrent(window,context))
            throw std::runtime_error(std::string("SDL_GL_MakeCurrent: ")+SDL_GetError());
        if(!smoke && !SDL_GL_SetSwapInterval(1))
            std::cerr<<"VSync unavailable: "<<SDL_GetError()<<'\n';
        renderer=std::make_unique<Renderer>();
        Room room;
        if(smoke_mode)room.configure(smoke_mode>=2,(smoke_mode&1)!=0);
        Pose player=room.spawn();
        float yaw=0.0f,pitch=0.0f;
        if(smoke_under) {
            player={3.6,-1.6,0.0};
            yaw=Pi/2.0f;
        }
        renderer->upload(labyrinth::demo::make_test_room_mesh(room.flight()));
        int w=1280,h=720;
        if(!SDL_GetWindowSizeInPixels(window,&w,&h))
            throw std::runtime_error(std::string("SDL_GetWindowSizeInPixels: ")+SDL_GetError());
        if(smoke) {
            renderer->draw(w,h,player,yaw,pitch);
            if(!screenshot_ppm(argv[2],w,h))
                throw std::runtime_error("headless screenshot failed / image was blank");
            std::cout<<"Rendered nonblank 3D screenshot: "<<argv[2]<<'\n';
            result=0;
        } else {
            std::cout<<"3D MAZE - PHYSICAL STAIR TEST ROOM\n"
                        "WASD move | mouse look | Shift run | R stairs | U underpass start\n"
                        "1 hollow/open | 2 hollow/walled | 3 solid/open | 4 solid/walled\n"
                        "Esc free/capture mouse | Left-click capture | Q quit\n"
                        "Walk straight up the steps or approach the HIGH end from its SIDE\n"
                        "to cross beneath. The solid and walled variants block that route.\n"
                        "This is a handcrafted test room, not the giant maze yet.\n";
            bool captured=SDL_SetWindowRelativeMouseMode(window,true);
            if(!captured)std::cerr<<"Mouse capture: "<<SDL_GetError()<<'\n';
            bool running=true;
            std::uint64_t previous=SDL_GetTicksNS(),title_tick=previous;
            while(running) {
                SDL_Event event;
                while(SDL_PollEvent(&event)) {
                    if(event.type==SDL_EVENT_QUIT)running=false;
                    if(event.type==SDL_EVENT_MOUSE_MOTION && captured) {
                        yaw-=event.motion.xrel*.0024f;
                        pitch=std::clamp(pitch-event.motion.yrel*.0024f,-1.48f,1.48f);
                    }
                    if(event.type==SDL_EVENT_MOUSE_BUTTON_DOWN && !captured)
                        captured=SDL_SetWindowRelativeMouseMode(window,true);
                    if(event.type!=SDL_EVENT_KEY_DOWN || event.key.repeat)continue;
                    if(event.key.scancode==SDL_SCANCODE_Q)running=false;
                    if(event.key.scancode==SDL_SCANCODE_ESCAPE) {
                        captured=!captured;
                        if(!SDL_SetWindowRelativeMouseMode(window,captured))
                            std::cerr<<"Mouse mode: "<<SDL_GetError()<<'\n';
                    }
                    if(event.key.scancode==SDL_SCANCODE_R) {
                        player=room.spawn();
                        yaw=0.0f;pitch=0.0f;
                    }
                    if(event.key.scancode==SDL_SCANCODE_U) {
                        player={3.6,-1.6,0.0};
                        yaw=Pi/2.0f;pitch=0.0f;
                    }
                    if(event.key.scancode>=SDL_SCANCODE_1 &&
                       event.key.scancode<=SDL_SCANCODE_4) {
                        const int mode=static_cast<int>(
                            event.key.scancode-SDL_SCANCODE_1);
                        room.configure(mode>=2,(mode&1)!=0);
                        // Swapping solid/walls in mid-underpass could trap
                        // the player inside new masonry; reset intentionally.
                        player=room.spawn();
                        renderer->upload(labyrinth::demo::make_test_room_mesh(room.flight()));
                        std::cout<<"Changed geometry: "<<(mode>=2?"SOLID":"HOLLOW")
                                 <<", "<<((mode&1)?"WALLED":"OPEN SIDES")<<'\n';
                    }
                }
                const std::uint64_t now=SDL_GetTicksNS();
                const double dt=std::min((now-previous)*1e-9,0.05);
                previous=now;
                const bool* k=SDL_GetKeyboardState(nullptr);
                const double ahead=(k[SDL_SCANCODE_W]?1.0:0.0)-
                                   (k[SDL_SCANCODE_S]?1.0:0.0);
                const double strafe=(k[SDL_SCANCODE_D]?1.0:0.0)-
                                    (k[SDL_SCANCODE_A]?1.0:0.0);
                const double norm=std::sqrt(ahead*ahead+strafe*strafe);
                if(norm>0) {
                    const double speed=(k[SDL_SCANCODE_LSHIFT]||
                                        k[SDL_SCANCODE_RSHIFT])?3.1:1.65;
                    const double dx=(std::cos(yaw)*ahead+std::sin(yaw)*strafe)*dt*speed/norm;
                    const double dy=(std::sin(yaw)*ahead-std::cos(yaw)*strafe)*dt*speed/norm;
                    room.move(player,dx,dy);
                }
                if(now-title_tick>180000000ULL) {
                    char title[240];
                    std::snprintf(title,sizeof(title),
                                  "3D Maze Stair Lab | %s / %s | x %.2f y %.2f floor %.2f | WASD, 1-4, R/U, Esc, Q",
                                  room.flight().solid_under?"SOLID":"HOLLOW",
                                  room.flight().sides==labyrinth::geometry::Sides::Walled?"WALLED":"OPEN",
                                  player.x,player.y,player.feet);
                    SDL_SetWindowTitle(window,title);
                    title_tick=now;
                }
                if(SDL_GetWindowSizeInPixels(window,&w,&h)) {
                    renderer->draw(w,h,player,yaw,pitch);
                    SDL_GL_SwapWindow(window);
                }
            }
            result=0;
        }
    } catch(...) {
        renderer.reset();
        if(context)SDL_GL_DestroyContext(context);
        if(window)SDL_DestroyWindow(window);
        SDL_Quit();
        throw;
    }
    renderer.reset();
    if(context)SDL_GL_DestroyContext(context);
    if(window)SDL_DestroyWindow(window);
    SDL_Quit();
    return result;
}
}

int main(int argc,char** argv) {
    try { return run(argc,argv); }
    catch(const std::exception& e) {
        std::cerr<<"3D Maze startup error: "<<e.what()<<'\n';
        return 1;
    }
}
