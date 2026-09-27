// Shared helpers for the Fluent for Mac launch videos. Everything is a pure function of time t,
// so render.py can screenshot any frame in any order.
const $ = id => document.getElementById(id);
const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
const ease = x => 1 - Math.pow(1 - clamp(x), 3);
const inout = x => { x = clamp(x); return x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2; };
const back = x => { x = clamp(x); const c = 1.7; return 1 + (c + 1) * Math.pow(x - 1, 3) + c * Math.pow(x - 1, 2); };
let G;

// Show one layer between [a, b); returns local time or -1.
function layer(id, a, b, t) {
  const el = $(id), on = t >= a && t < b;
  el.style.opacity = on ? 1 : 0;
  el.style.visibility = on ? "visible" : "hidden";
  return on ? t - a : -1;
}
// Pop in (scale + blur) at local time `at`.
function pop(el, lt, at, from = 1.3, dur = 0.16) {
  const k = clamp((lt - at) / dur);
  el.style.opacity = k;
  el.style.transform = `scale(${from + (1 - from) * back(k)})`;
  el.style.filter = k < 1 ? `blur(${(1 - k) * 10}px)` : "none";
}
// Slide up + fade in.
function rise(el, lt, at = 0, dist = 80, dur = 0.3) {
  const k = ease((lt - at) / dur);
  el.style.opacity = clamp((lt - at) / (dur * 0.5));
  el.style.transform = `translateY(${(1 - k) * dist}px)`;
}
// Rubber stamp: drops from big and tilted, lands with a jolt.
function stamp(el, lt, at, rot = -8) {
  const k = clamp((lt - at) / 0.14);
  el.style.opacity = k > 0 ? 1 : 0;
  const s = 2.4 - 1.4 * ease(k);
  el.style.transform = `rotate(${rot}deg) scale(${s})`;
}
// Draw an SVG path on (stroke-dashoffset), from local time `at` over `dur`.
function draw(el, lt, at, dur) {
  const len = el.getTotalLength ? el.getTotalLength() : 1000;
  el.style.strokeDasharray = len;
  el.style.strokeDashoffset = len * (1 - ease((lt - at) / dur));
}
function beats(n) { return n * G.beat; }
