// Offline design calculations. Not a Unity rendering/performance test.
// Run: node docs/plans/evidence/2026-10-11-pc-foam-research/design-calculations.cjs
const fs = require('fs');
const path = require('path');
const rot = (p, a) => [Math.cos(a)*p[0]-Math.sin(a)*p[1], Math.sin(a)*p[0]+Math.cos(a)*p[1], p[2]];
const add = (a,b) => a.map((x,i)=>x+b[i]);
const mul = (a,s) => a.map(x=>x*s);
const distance = (a,b) => Math.hypot(...a.map((x,i)=>x-b[i]));
const p0=[0.04,0,0], p1=[0,0.04,0], midpoint=mul(add(p0,p1),0.5);
const skinExamples=[0,90,180].map(deg=>{
  const angle=deg*Math.PI/180;
  const correctSurfaceMidpoint=mul(add(p0,rot(p1,angle)),0.5);
  const skinnedInterpolatedAnchor=mul(add(midpoint,rot(midpoint,angle)),0.5);
  return {boneAngleDegrees:deg, midpointSkinErrorMeters:distance(correctSurfaceMidpoint,skinnedInterpolatedAnchor), blendedNormalLength:Math.hypot(...mul(add([1,0,0],rot([1,0,0],angle)),0.5))};
});
const shibaArea=0.8182086; // Editor read-only: source bind mesh area scaled to metres; not visible foam area.
const capacity=[64,128,192,256].map(lobes=>({lobes,triangles:lobes*80,vertices:lobes*42, footprintSumM2:lobes*Math.PI*0.025**2, footprintToShibaTotalArea:lobes*Math.PI*0.025**2/shibaArea, estimatedBytes:lobes*(42*128+80*3*2)}));
const sixBodyTriangles=[6548,5140,6320,3764,6408,4596].reduce((a,b)=>a+b,0);
const report={kind:'offline arithmetic, not measured GPU/CPU time or visual coverage',skinExamples,capacity,sixBodyTriangles,nominalSixDogsCurrentPassTriangles:5*sixBodyTriangles,nominalSixDogsCandidate192:2*sixBodyTriangles+6*192*80};
if(skinExamples[1].midpointSkinErrorMeters<=0 || skinExamples[2].blendedNormalLength>1e-12) throw new Error('Counterexample invalid');
fs.writeFileSync(path.join(__dirname,'design-calculations.json'),JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify(report,null,2));
