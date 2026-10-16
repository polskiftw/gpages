// WebGL1 renderer; four built-in repeating surface materials, no assets or third-party libraries.
const vertex="\nattribute vec3 aPosition;\nattribute vec3 aNormal;\nattribute vec2 aUV;\nattribute float aMaterial;\nuniform mat4 uViewProjection;\nvarying vec3 vPosition,vNormal;\nvarying vec2 vUV;\nvarying float vMaterial;\nvoid main(){\n  vPosition=aPosition;vNormal=aNormal;vUV=aUV;vMaterial=aMaterial;\n  gl_Position=uViewProjection*vec4(aPosition,1.0);\n}";
const fragment="\nprecision highp float;\nvarying vec3 vPosition,vNormal;\nvarying vec2 vUV;\nvarying float vMaterial;\nuniform vec3 uEye;\nfloat hash(vec2 n){return fract(sin(dot(n,vec2(127.1,311.7)))*43758.5453123);}\nvec3 pattern(vec2 t,float mat){\n  if(mat<0.5){\n    float row=floor(t.y),shift=mod(row,2.0)*0.5;\n    vec2 b=vec2(t.x+shift,t.y);\n    float grout=step(0.055,fract(b.x))*step(0.075,fract(b.y));\n    float grain=hash(floor(b))*0.14+0.76;\n    return mix(vec3(0.35,0.33,0.31),vec3(0.54,0.47,0.41)*grain,grout);\n  }\n  if(mat<1.5){\n    vec2 cell=floor(t*0.6),f=fract(t*0.6);\n    float joint=step(0.045,f.x)*step(0.045,f.y);\n    return mix(vec3(0.28,0.29,0.29),vec3(0.53,0.53,0.49)*(0.83+0.17*hash(cell)),joint);\n  }\n  if(mat<2.5){\n    float speck=hash(floor(t*1.8))*0.07;\n    return vec3(0.41,0.40,0.37)+speck;\n  }\n  if(mat<3.5){\n    float tread=step(0.115,fract(t.y));\n    float rough=0.87+0.13*hash(floor(t));\n    return mix(vec3(0.18,0.19,0.19),vec3(0.50,0.49,0.45)*rough,tread);\n  }\n  return vec3(0.32,0.60,0.48);\n}\nvoid main(){\n  vec3 base=pattern(vUV,vMaterial);\n  vec3 n=normalize(vNormal);\n  float light=0.48+0.45*abs(dot(n,normalize(vec3(0.38,0.87,0.24))));\n  float d=distance(vPosition,uEye);\n  float lamp=0.70+0.48/(1.0+0.006*d*d);\n  vec3 color=base*light*lamp;\n  float fog=clamp((d-37.0)/23.0,0.0,1.0);\n  color=mix(color,vec3(0.035,0.041,0.046),fog);\n  gl_FragColor=vec4(color,1.0);\n}";

const shader=(gl,type,source)=>{
  const s=gl.createShader(type);gl.shaderSource(s,source);gl.compileShader(s);
  if(!gl.getShaderParameter(s,gl.COMPILE_STATUS))
    throw new Error("WebGL shader: "+gl.getShaderInfoLog(s));
  return s;
};
const program=(gl)=>{
  const p=gl.createProgram();
  gl.attachShader(p,shader(gl,gl.VERTEX_SHADER,vertex));
  gl.attachShader(p,shader(gl,gl.FRAGMENT_SHADER,fragment));
  gl.linkProgram(p);
  if(!gl.getProgramParameter(p,gl.LINK_STATUS))
    throw new Error("WebGL program: "+gl.getProgramInfoLog(p));
  return p;
};
function multiply(a,b){
  const r=new Float32Array(16);
  for(let col=0;col<4;col++)for(let row=0;row<4;row++){
    let sum=0;for(let k=0;k<4;k++)sum+=a[k*4+row]*b[col*4+k];
    r[col*4+row]=sum;
  }
  return r;
}
function matrix(eye,yaw,pitch,aspect){
  const fov=Math.PI/2.55,near=0.07,far=90,
    s=1/Math.tan(fov/2),cp=Math.cos(pitch),sp=Math.sin(pitch),
    cy=Math.cos(yaw),sy=Math.sin(yaw);
  const projection=new Float32Array([
    s/aspect,0,0,0, 0,s,0,0,
    0,0,(far+near)/(near-far),-1,
    0,0,2*far*near/(near-far),0
  ]);
  const right=[cy,0,sy],up=[-sy*sp,cp,cy*sp],forward=[sy*cp,sp,-cy*cp];
  const dot=a=>a[0]*eye[0]+a[1]*eye[1]+a[2]*eye[2];
  const view=new Float32Array([
    right[0],up[0],-forward[0],0,
    right[1],up[1],-forward[1],0,
    right[2],up[2],-forward[2],0,
    -dot(right),-dot(up),dot(forward),1
  ]);
  return multiply(projection,view);
}
export class MazeWebGL{
  constructor(canvas){
    this.canvas=canvas;
    const gl=canvas.getContext("webgl",{alpha:false,antialias:true,powerPreference:"high-performance"});
    if(!gl)throw new Error("This browser does not provide WebGL. Try an up-to-date browser or enable hardware acceleration.");
    this.gl=gl;this.program=program(gl);gl.useProgram(this.program);
    this.buffer=gl.createBuffer();this.count=0;this.triangles=0;
    this.cameraLoc=gl.getUniformLocation(this.program,"uEye");
    this.viewLoc=gl.getUniformLocation(this.program,"uViewProjection");
    gl.enable(gl.DEPTH_TEST);gl.depthFunc(gl.LEQUAL);gl.disable(gl.CULL_FACE);
    gl.clearColor(0.035,0.041,0.046,1.0);
    const stride=9*4,attr=(name,n,offset)=>{
      const i=gl.getAttribLocation(this.program,name);
      gl.enableVertexAttribArray(i);gl.vertexAttribPointer(i,n,gl.FLOAT,false,stride,offset*4);
    };
    gl.bindBuffer(gl.ARRAY_BUFFER,this.buffer);
    attr("aPosition",3,0);attr("aNormal",3,3);attr("aUV",2,6);attr("aMaterial",1,8);
  }
  setMesh(mesh){
    const gl=this.gl;this.count=mesh.vertices.length/9;this.triangles=this.count/3;
    gl.bindBuffer(gl.ARRAY_BUFFER,this.buffer);
    gl.bufferData(gl.ARRAY_BUFFER,mesh.vertices,gl.STATIC_DRAW);
  }
  draw(pose,eyeHeight){
    const gl=this.gl,canvas=this.canvas;
    const pixelRatio=Math.min(1.8,window.devicePixelRatio||1),
      w=Math.max(1,Math.floor(canvas.clientWidth*pixelRatio)),
      h=Math.max(1,Math.floor(canvas.clientHeight*pixelRatio));
    if(canvas.width!==w||canvas.height!==h){canvas.width=w;canvas.height=h;}
    gl.viewport(0,0,w,h);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);
    gl.useProgram(this.program);
    const eye=[pose.x,pose.feet+eyeHeight,pose.z];
    gl.uniform3fv(this.cameraLoc,eye);
    gl.uniformMatrix4fv(this.viewLoc,false,matrix(eye,pose.yaw,pose.pitch,w/h));
    gl.bindBuffer(gl.ARRAY_BUFFER,this.buffer);
    gl.drawArrays(gl.TRIANGLES,0,this.count);
  }
}
