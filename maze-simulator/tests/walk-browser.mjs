// A *real browser* smoke test: renders WebGL, starts a seeded maze,
// moves the camera, and captures the first-person result. No npm dependency
// ships with the actual game; CI installs a temporary playwright-core.
import assert from "node:assert/strict";
import {pathToFileURL} from "node:url";
const path=process.env.MAZE_PLAYWRIGHT||"/tmp/maze-browser/node_modules/playwright-core/index.mjs";
const {chromium}=await import(pathToFileURL(path).href);
const browser=await chromium.launch({
  executablePath:process.env.MAZE_CHROME||"/usr/bin/google-chrome",
  headless:true,args:["--no-sandbox","--enable-webgl","--ignore-gpu-blocklist",
    "--enable-unsafe-swiftshader","--use-angle=swiftshader"]
});
try{
  const page=await browser.newPage({viewport:{width:1280,height:860},deviceScaleFactor:1});
  const errs=[];
  page.on("pageerror",err=>errs.push(err.stack||err.message));
  page.on("console",msg=>{if(msg.type()==="error")errs.push(msg.text());});
  const base="http://127.0.0.1:8765/maze-simulator/walk/";
  await page.goto(base+"?s=LOST-6549&w=28&h=28&l=6&stairs=2",{waitUntil:"networkidle"});
  await page.waitForFunction(()=>{
    const c=document.querySelector("#view");
    return !!c&&c.width>500&&c.height>250;
  },{timeout:30000});
  assert.equal(await page.locator("#error").isVisible(),false,"no error alert when generating seed");
  const initial=await page.locator("#stats").innerText();
  assert.ok(initial.includes("LOST-6549"),"seed-loaded 3D world is displayed");
  const gl=await page.evaluate(()=>{
    const c=document.getElementById("view"),gl=c.getContext("webgl");
    return {ok:!!gl,buffer:c.width*c.height,version:gl?.getParameter(gl.VERSION),
      error:gl?.getError()};
  });
  assert.equal(gl.ok,true,"first-person WebGL initialized");
  assert.equal(gl.error,0,"no WebGL error during initialization");
  await page.locator("#coordinates").check();
  await page.waitForTimeout(300);
  const before=await page.locator("#stats").innerText();
  await page.keyboard.down("w");await page.waitForTimeout(950);await page.keyboard.up("w");
  await page.waitForTimeout(450);
  const after=await page.locator("#stats").innerText();
  assert.notEqual(before,after,"pressing W advances the actual 3D position");
  await page.locator("#view").screenshot({path:"/tmp/maze-3d-browser.png"});
  const params=await page.evaluate(()=>new URLSearchParams(location.search).get("s"));
  assert.equal(params,"LOST-6549","page preserves the exact reproducible maze seed");
  await page.locator("#restart").click();
  await page.waitForTimeout(450);
  const back=await page.locator("#stats").innerText();
  assert.notEqual(back,after,"restart goes back to original entrance");
  assert.deepEqual(errs,[],"no runtime JS errors");
  console.log("BROWSER 3D PASS",JSON.stringify({viewport:"1280x860",gl,initial,before,after,restarted:back}));
}finally{await browser.close();}
