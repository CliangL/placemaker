#!/bin/sh
# gal.sh <outdir> <sites>: the kept shops styles with the suite's entrance sets, offline.
out=${1:-.forensics/gal}; sites=${2:-gblock-150x140-20_20_12_12}
cd /c/Users/jack/pm-wt/rf-shops3
rm -rf $out; mkdir -p $out
run() { dotnet run -c Release --no-build --project Tests/Placemaker.Gallery -- --out $out/$1 --only $1 --presets $2 --sites $sites --markers "$3" > $out/$1.log 2>&1; }
run stripMall classic,bigBox "entrance@s:0.3,entrance@s:0.72,footpath@s:0.5,alley@n:0.5,exit@e:0.55" &
run mallDistrict district,dense "entrance@s:0.5,entrance@n:0.5,footpath@e:0.5,footpath@w:0.5,alley@e:0.25" &
run mallPark commons,garden,family "entrance@s:0.5,alley@e:0.5,footpath@w:0.5,footpath@n:0.5" &
wait
for t in stripMall mallDistrict mallPark; do node -e "
const g=JSON.parse(require('fs').readFileSync('$out/$t/gallery.json','utf8'));for(const it of (g.items||g)){ if(!/preset/.test(it.variant))continue; const f=it.facts||{}; console.log(it.type, it.variant, 'plots', f.shopPlots, 'lots', f.lots, 'cap', f.capacity, 'gates', f.gates+'/'+f.gatesLanded, (it.issues||[]).join(' '), it.failure||'')}"; done
