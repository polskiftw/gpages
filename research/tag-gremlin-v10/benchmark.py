#!/usr/bin/env python3
from __future__ import annotations
import argparse, json, math, random, statistics, time, heapq
from dataclasses import dataclass, asdict
from collections import defaultdict, Counter
from typing import Dict, List, Tuple, Set, Optional

K=40
ROOT='abcdefghijklmnopqrstuvwxyz0123456789'
NEXT='abcdefghijklmnopqrstuvwxyz0123456789.'
MAX_SUB=4

COLORS='black white red blue green pink purple orange yellow brown gray silver gold blonde cyan teal violet'.split()
BODY='hair eyes skin face mouth lips teeth tongue ears hands feet legs arms breasts hips waist belly thighs tail wings horns'.split()
LOOK='long short curly straight messy shiny pale dark light soft thick thin small large huge tiny cute pretty handsome mature young old'.split()
CLOTH='dress skirt shirt sweater jacket coat uniform bikini swimsuit stockings socks gloves boots shoes heels hat cap ribbon glasses armor'.split()
POSE='standing sitting kneeling lying walking running jumping leaning looking smiling crying laughing sleeping dancing fighting'.split()
OBJ='sword gun book phone camera cup food flower chair bed car bike umbrella bag ball rope chain sign'.split()
SCENE='indoors outdoors beach forest city street room bedroom school office night sunset rain snow water sky'.split()
CAM='closeup portrait profile sideview frombehind fromabove frombelow fullbody upperbody solo duo group'.split()
EXPR='happy sad angry embarrassed surprised scared smug serious sleepy blush openmouth grin frown'.split()
MORPH='neo cyber mega mini ultra hyper dark light star moon sun fire ice aqua techno magic dream shadow angel demon cat dog fox bunny wolf dragon'.split()
NAMES='alice aria asuka ayaka bella celeste chloe diana emma eri hana haruka hina iris jane jill julia kana karin katie kyoko lena lily luna maria maya mika miku mina mio misa momo nana nina rei rena rin rina rose ruby sakura sara sora yuki yuna'.split()
FRANCH='academy chronicles project girls world legends fantasy quest club story online zero xiii galaxy saga'.split()
TRANS='akari ayame chihaya kaede koharu kotone madoka makoto megumi nanami nozomi sayaka shiori takumi tomoe tsukasa yuriko'.split()
ABBR='ai cg oc pov rpg fps npc vr ui hd 2d 3d sfw nsfw'.split()

ARCHETYPES={
    'balanced': dict(ling=0.72, proper=0.13, weird=0.05, short=0.05, punct=0.05, compound=0.48, reuse=0.72, skew=1.10),
    'linguistic':dict(ling=0.84, proper=0.08, weird=0.02, short=0.03, punct=0.03, compound=0.60, reuse=0.88, skew=1.18),
    'names':dict(ling=0.52, proper=0.28, weird=0.06, short=0.06, punct=0.08, compound=0.42, reuse=0.60, skew=1.02),
    'weird':dict(ling=0.50, proper=0.12, weird=0.18, short=0.10, punct=0.10, compound=0.35, reuse=0.42, skew=0.93),
    'shortheavy':dict(ling=0.60, proper=0.12, weird=0.06, short=0.17, punct=0.05, compound=0.38, reuse=0.62, skew=1.07),
    'compound':dict(ling=0.75, proper=0.08, weird=0.03, short=0.03, punct=0.11, compound=0.78, reuse=0.83, skew=1.13),
}

def clean(s:str)->str:
    s=''.join(ch for ch in s.lower() if ch.isalnum() or ch=='.')
    while s and s[0]=='.': s=s[1:]
    return s or 'aa'

def rand_weird(r:random.Random)->str:
    n=r.randint(3,13)
    alpha='abcdefghijklmnopqrstuvwxyz0123456789'
    s=''.join(r.choice(alpha) for _ in range(n))
    if n>5 and r.random()<.25:
        i=r.randint(1,n-2); s=s[:i]+'.'+s[i:]
    return s

def make_linguistic(r:random.Random, cfg)->str:
    pools=[COLORS,BODY,LOOK,CLOTH,POSE,OBJ,SCENE,CAM,EXPR,MORPH]
    a=r.choice(r.choice(pools))
    if r.random()<cfg['compound']:
        b=r.choice(r.choice(pools))
        if b==a: b=r.choice(MORPH)
        # Preserve the old total separator probability, but map every supported
        # punctuation opportunity to the site's only punctuation character: '.'.
        sep=r.choices(['','.'],[.72,.28])[0] if r.random()<cfg['punct']*2.5 else ''
        if r.random()<.35: return clean(a+sep+b)
        return clean(b+sep+a)
    if r.random()<.18: a=r.choice(MORPH)+a
    return clean(a)

def make_proper(r:random.Random,cfg)->str:
    a=r.choice(NAMES+TRANS)
    if r.random()<.6:
        b=r.choice(FRANCH+NAMES+MORPH)
        # Old generator chose '', '-' or '.' uniformly after this gate. With '-'
        # unsupported, both punctuation branches become '.'.
        sep=r.choice(['','.','.']) if r.random()<cfg['punct']*3 else ''
        return clean(a+sep+b)
    return a

def make_short(r):
    if r.random()<.72:
        return ''.join(r.choice('abcdefghijklmnopqrstuvwxyz') for _ in range(2))
    return r.choice(ABBR + [str(r.randint(10,99)), r.choice('abcdefghijklmnopqrstuvwxyz')+str(r.randint(0,9))])

def mutate(base:str,r:random.Random)->str:
    mode=r.randrange(5)
    if mode==0: return clean(base+r.choice(MORPH+LOOK+BODY))
    if mode==1: return clean(r.choice(MORPH+COLORS)+base)
    if mode==2: return clean(base+str(r.randint(1,999)))
    if mode==3 and len(base)>3:
        i=r.randint(1,len(base)-1); return clean(base[:i]+r.choice('aeiouy')+base[i:])
    return clean(base+'.'+r.choice(MORPH+NAMES))
