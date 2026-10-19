#!/usr/bin/env python3
"""Offline fireball simulation for Volumetric Explosion FX (original code, numpy only).

A small 3D gas simulation (stable fluids: semi-Lagrangian/MacCormack advection, buoyancy, hot-gas
expansion, vorticity confinement, exact pressure solve by separable eigen-decomposition) bakes an
explosion into density / temperature / soot fields. The renderer samples the baked fields and adds
fine detail on top. Nothing here is taken from another project.

Units: lengths in "R" (about the fireball radius), times in the simulation's own unit.
Output: float32 array frames[N, nx, ny, nz, 3] = (gas, temperature, soot), plus times and bounds.
"""
import argparse, hashlib, json, math, os, sys, time
import numpy as np

VERSION = 'firesim-v1'


class Params:
    def __init__(self, **kw):
        self.ground = True
        self.nx, self.ny, self.nz = 64, 80, 64
        self.half = 1.6              # half width (x,z) in R
        self.height = 4.0            # height in R (ground) / full height (air)
        self.r0 = 0.30               # initial hot sphere radius
        self.source_y = 0.35         # centre height of the source (ground), in R
        self.expand = 5.0            # hot-gas expansion rate
        self.expand_pow = 1.5
        self.buoy = 2.2              # buoyancy per unit temperature
        self.cool = 0.9              # radiative cooling
        self.turb = 1.2              # turbulence forcing amplitude
        self.vort = 0.5              # vorticity confinement strength
        self.soot_rate = 1.6
        self.feed = 0.0              # sustained hot gas fed at the base (ground) for a stem / column
        self.feed_time = 1.6
        self.tmax = 4.0
        self.frames = 28
        self.frame_pow = 1.5
        self.dt_max = 0.02
        self.cfl = 1.2
        self.seed = 1
        self.stable_at = 0.5         # share of the height where the stable air starts (the cap spreads under it)
        self.stable = 0.0            # extra: how hard the stable air stops the rise (0 = the original gentle cap)
        self.wobble = 1.0            # how irregular the starting fireball is (1 = the original)
        self.turb_ramp = 0.0         # seconds over which the turbulence builds up (0 = at once, the original)
        self.__dict__.update(kw)


def trilinear(shape, x, y, z):
    """Indices and weights for sampling cell-centred arrays at grid positions (clamped)."""
    nx, ny, nz = shape
    x = np.clip(x, 0, nx - 1.001); y = np.clip(y, 0, ny - 1.001); z = np.clip(z, 0, nz - 1.001)
    i = x.astype(np.int32); j = y.astype(np.int32); k = z.astype(np.int32)
    fx = (x - i).astype(np.float32); fy = (y - j).astype(np.float32); fz = (z - k).astype(np.float32)
    base = (i * ny + j) * nz + k
    sx, sy = ny * nz, nz
    idx = [base, base + 1, base + sy, base + sy + 1, base + sx, base + sx + 1, base + sx + sy, base + sx + sy + 1]
    w = [(1 - fx) * (1 - fy) * (1 - fz), (1 - fx) * (1 - fy) * fz, (1 - fx) * fy * (1 - fz), (1 - fx) * fy * fz,
         fx * (1 - fy) * (1 - fz), fx * (1 - fy) * fz, fx * fy * (1 - fz), fx * fy * fz]
    return idx, w


def apply(interp, f):
    idx, w = interp
    flat = f.ravel()
    out = flat[idx[0]] * w[0]
    for a in range(1, 8):
        out += flat[idx[a]] * w[a]
    return out.reshape(f.shape)


def laplacian_1d(n, dx, wall_low):
    """1D operator with p=0 at the open faces (ghost = -p) or zero gradient at the wall (ghost = p)."""
    m = np.zeros((n, n), dtype=np.float64)
    for a in range(n):
        m[a, a] = -2
        if a > 0: m[a, a - 1] = 1
        if a < n - 1: m[a, a + 1] = 1
    m[n - 1, n - 1] -= 1                      # open face: ghost = -p
    m[0, 0] += 0 if wall_low else -1          # wall: ghost = p  (adds +1 -> -1); open: -3
    if wall_low: m[0, 0] += 1
    return m / (dx * dx)


class Sim:
    def __init__(self, p):
        self.p = p
        nx, ny, nz = p.nx, p.ny, p.nz
        self.shape = (nx, ny, nz)
        self.dx = 2 * p.half / nx
        self.dy = p.height / ny
        assert abs(self.dx - self.dy) < 1e-6, 'cubic cells required'
        self.rng = np.random.RandomState(p.seed)
        X, Y, Z = np.meshgrid(np.arange(nx, dtype=np.float32), np.arange(ny, dtype=np.float32), np.arange(nz, dtype=np.float32), indexing='ij')
        self.gx, self.gy, self.gz = X, Y, Z
        # physical coordinates of cell centres (x,z centred; y from 0 for ground, centred otherwise)
        self.px = (X + 0.5) * self.dx - p.half
        self.pz = (Z + 0.5) * self.dx - p.half
        self.py = (Y + 0.5) * self.dx - (0 if p.ground else p.height / 2)
        self.u = np.zeros(self.shape, np.float32); self.v = np.zeros_like(self.u); self.w = np.zeros_like(self.u)
        self.T = np.zeros_like(self.u); self.G = np.zeros_like(self.u); self.S = np.zeros_like(self.u)
        # pressure solver: eigen-decomposition of the three 1D operators
        lam, Q = [], []
        for n, wall in ((nx, False), (ny, p.ground), (nz, False)):
            l, q = np.linalg.eigh(laplacian_1d(n, self.dx, wall))
            lam.append(l); Q.append(q)
        self.lam, self.Q = lam, Q
        self.denom = (lam[0][:, None, None] + lam[1][None, :, None] + lam[2][None, None, :])
        self.noise_force = self.make_noise_forcing()
        self.init_fields()
        self.time = 0.0

    # ---- noise ---------------------------------------------------------------------------------
    def smooth_noise(self, cells, seed):
        rng = np.random.RandomState(seed)
        nx, ny, nz = self.shape
        cx = max(2, int(round(cells * 2 * self.p.half / 1.0))); cy = max(2, int(round(cells * self.p.height / 1.0)))
        g = rng.randn(cx + 3, cy + 3, cx + 3).astype(np.float32)
        fx = np.linspace(0, cx - 0.001, nx); fy = np.linspace(0, cy - 0.001, ny); fz = np.linspace(0, cx - 0.001, nz)
        X, Y, Z = np.meshgrid(fx, fy, fz, indexing='ij')
        i, j, k = X.astype(int), Y.astype(int), Z.astype(int)
        tx, ty, tz = X - i, Y - j, Z - k
        tx, ty, tz = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty), tz * tz * (3 - 2 * tz)
        out = 0
        for a in (0, 1):
            for b in (0, 1):
                for c in (0, 1):
                    wgt = (tx if a else 1 - tx) * (ty if b else 1 - ty) * (tz if c else 1 - tz)
                    out = out + wgt * g[i + a, j + b, k + c]
        return out.astype(np.float32)

    def make_noise_forcing(self):
        """Divergence-free turbulence forcing: curl of a vector potential of two octaves."""
        pot = []
        for c in range(3):
            pot.append(self.smooth_noise(1.6, 100 + c) * 1.0 + self.smooth_noise(3.4, 200 + c) * 0.55 + self.smooth_noise(7.0, 300 + c) * 0.28)
        d = self.dx
        def g(f, ax): return np.gradient(f, d, axis=ax)
        fx = g(pot[2], 1) - g(pot[1], 2)
        fy = g(pot[0], 2) - g(pot[2], 0)
        fz = g(pot[1], 0) - g(pot[0], 1)
        s = max(np.abs(fx).max(), np.abs(fy).max(), np.abs(fz).max()) + 1e-6
        return (fx / s, fy / s, fz / s)

    def init_fields(self):
        p = self.p
        cy = p.source_y if p.ground else (-p.height / 2 + p.source_y)
        r = np.sqrt(self.px ** 2 + (self.py - cy) ** 2 + self.pz ** 2)
        n1 = self.smooth_noise(2.5, 11); n2 = self.smooth_noise(6.0, 12)
        wobble = (0.16 * n1 + 0.08 * n2) * p.wobble
        edge = (p.r0 * (1 + wobble) - r) / (0.07)
        self.G = np.clip(0.5 + 0.5 * edge, 0, 1).astype(np.float32)
        self.T = (self.G * (0.92 + 0.08 * n2)).astype(np.float32)
        if p.ground: self.G[:, :1, :] = np.maximum(self.G[:, :1, :], 0)

    # ---- core steps ----------------------------------------------------------------------------
    def face_velocity(self):
        nx, ny, nz = self.shape
        def faces(c, axis, wall_low=False):
            sh = list(c.shape); sh[axis] += 1
            f = np.empty(sh, np.float32)
            lo = [slice(None)] * 3; hi = [slice(None)] * 3; mid = [slice(None)] * 3
            f_mid = [slice(None)] * 3
            a = [slice(None)] * 3; b = [slice(None)] * 3
            a[axis] = slice(0, -1); b[axis] = slice(1, None)
            m = [slice(None)] * 3; m[axis] = slice(1, -1)
            f[tuple(m)] = 0.5 * (c[tuple(a)] + c[tuple(b)])
            e0 = [slice(None)] * 3; e0[axis] = 0
            e1 = [slice(None)] * 3; e1[axis] = -1
            f[tuple(e0)] = 0 if wall_low else c[tuple(e0)]
            f[tuple(e1)] = c[tuple(e1)]
            return f
        return faces(self.u, 0), faces(self.v, 1, self.p.ground), faces(self.w, 2)

    def project(self, dt, Q):
        nx, ny, nz = self.shape
        dx = self.dx
        uf, vf, wf = self.face_velocity()
        div = (uf[1:] - uf[:-1] + vf[:, 1:] - vf[:, :-1] + wf[:, :, 1:] - wf[:, :, :-1]) / dx
        rhs = ((div - Q) / dt).astype(np.float64)
        r = np.tensordot(self.Q[0].T, rhs, axes=(1, 0))
        r = np.tensordot(self.Q[1].T, r, axes=(1, 1)).transpose(1, 0, 2)
        r = np.tensordot(self.Q[2].T, r, axes=(1, 2)).transpose(1, 2, 0)
        r /= self.denom
        pr = np.tensordot(self.Q[0], r, axes=(1, 0))
        pr = np.tensordot(self.Q[1], pr, axes=(1, 1)).transpose(1, 0, 2)
        pr = np.tensordot(self.Q[2], pr, axes=(1, 2)).transpose(1, 2, 0)
        pr = pr.astype(np.float32)
        # pressure with ghost cells: open faces p_ghost = -p, wall p_ghost = +p
        def ghost(axis, wall_low):
            lo = np.take(pr, [0], axis=axis); hi = np.take(pr, [-1], axis=axis)
            return np.concatenate([lo if wall_low else -lo, pr, -hi], axis=axis)
        gx = ghost(0, False); gy = ghost(1, self.p.ground); gz = ghost(2, False)
        uf -= dt * (gx[1:] - gx[:-1]) / dx
        vf -= dt * (gy[:, 1:] - gy[:, :-1]) / dx
        wf -= dt * (gz[:, :, 1:] - gz[:, :, :-1]) / dx
        if self.p.ground: vf[:, 0, :] = 0
        self.u = 0.5 * (uf[1:] + uf[:-1]); self.v = 0.5 * (vf[:, 1:] + vf[:, :-1]); self.w = 0.5 * (wf[:, :, 1:] + wf[:, :, :-1])

    def vorticity_force(self, dt):
        d = self.dx
        def g(f, ax): return np.gradient(f, d, axis=ax)
        wx = g(self.w, 1) - g(self.v, 2); wy = g(self.u, 2) - g(self.w, 0); wz = g(self.v, 0) - g(self.u, 1)
        mag = np.sqrt(wx * wx + wy * wy + wz * wz)
        nx_, ny_, nz_ = g(mag, 0), g(mag, 1), g(mag, 2)
        n = np.sqrt(nx_ ** 2 + ny_ ** 2 + nz_ ** 2) + 1e-5
        nx_, ny_, nz_ = nx_ / n, ny_ / n, nz_ / n
        fx = ny_ * wz - nz_ * wy; fy = nz_ * wx - nx_ * wz; fz = nx_ * wy - ny_ * wx
        k = self.p.vort * d
        hot = np.clip(self.G * 2, 0, 1)
        self.u += dt * k * fx * hot * 4; self.v += dt * k * fy * hot * 4; self.w += dt * k * fz * hot * 4

    def advect(self, dt):
        x, y, z = self.gx, self.gy, self.gz
        inv = 1.0 / self.dx
        itp0 = trilinear(self.shape, x, y, z)
        # RK2 back-trace on the cell-centred velocity
        u0, v0, w0 = self.u, self.v, self.w
        xm = x - 0.5 * dt * u0 * inv; ym = y - 0.5 * dt * v0 * inv; zm = z - 0.5 * dt * w0 * inv
        itpm = trilinear(self.shape, xm, ym, zm)
        um, vm, wm = apply(itpm, u0), apply(itpm, v0), apply(itpm, w0)
        xb = x - dt * um * inv; yb = y - dt * vm * inv; zb = z - dt * wm * inv
        back = trilinear(self.shape, xb, yb, zb)
        # forward positions for MacCormack error correction
        xf = x + dt * um * inv; yf = y + dt * vm * inv; zf = z + dt * wm * inv
        fwd = trilinear(self.shape, xf, yf, zf)
        newu, newv, neww = apply(back, u0), apply(back, v0), apply(back, w0)
        out = []
        for f in (self.T, self.G, self.S):
            adv = apply(back, f)
            est = apply(fwd, adv)                     # advect back the advected field (forward trace)
            corrected = adv + 0.5 * (f - est)
            # limiter: stay within the local min/max of the departure neighbourhood
            lo = np.minimum.reduce([f.ravel()[ix] for ix in back[0]]).reshape(f.shape)
            hi = np.maximum.reduce([f.ravel()[ix] for ix in back[0]]).reshape(f.shape)
            out.append(np.clip(corrected, lo, hi))
        self.T, self.G, self.S = out
        self.u, self.v, self.w = newu, newv, neww

    def step(self, dt):
        p = self.p
        t = self.time
        self.advect(dt)
        # hot-gas expansion source and buoyancy / turbulence forcing
        hot = np.clip(self.T, 0, 1)
        Q = p.expand * (np.clip((hot - 0.4) / 0.6, 0, 1) ** p.expand_pow) * np.clip(self.G * 1.5, 0, 1)
        # keep the source from expanding the cool far field
        # Stable air aloft: the hot gas stops rising near its equilibrium height and spreads into a cap
        # (so the cloud stays in the box instead of leaving through its top).
        yb = (self.gy + 0.5) * self.dx
        cap = np.clip((yb - p.stable_at * p.height) / ((1 - p.stable_at) * p.height), 0, 1).astype(np.float32)
        self.v += dt * (p.buoy * hot * self.G * (1 - cap))
        self.v *= (1 - np.clip(dt * 0.7 * cap * cap, 0, 0.5)).astype(np.float32)   # gentle: a rounded cap, not a sheet
        if p.stable > 0:
            # Strongly stable air (the tropopause for a nuclear cloud): rising gas overshoots a little, is pushed back and
            # spreads sideways into a broad cap.
            over = np.clip((yb - p.stable_at * p.height) / (0.15 * p.height), 0, 1).astype(np.float32)
            self.v -= dt * p.stable * over * np.maximum(self.v, 0) * np.clip(self.G * 2, 0, 1)
        fade = math.exp(-t / 1.4)
        tf = p.turb * hot * np.clip(self.G * 1.5, 0, 1) * (0.25 + fade)
        if p.turb_ramp > 0: tf = tf * min(1.0, t / p.turb_ramp)
        fx, fy, fz = self.noise_force
        # slowly drifting forcing pattern: shift by rolling with time (cheap decorrelation)
        shift = int(t * 6) % 11
        self.u += dt * tf * np.roll(fx, shift, axis=1); self.v += dt * tf * np.roll(fy, shift, axis=2); self.w += dt * tf * np.roll(fz, shift, axis=0)
        if p.feed > 0 and t < p.feed_time:
            amp = p.feed * (1 - t / p.feed_time)
            cyl = np.exp(-((self.px ** 2 + self.pz ** 2) / 0.22)) * np.exp(-(((self.py - (p.source_y if p.ground else -p.height / 2 + p.source_y)) / 0.35) ** 2 if not p.ground else (self.py / 0.35) ** 2))
            self.G = np.maximum(self.G, np.clip(amp * cyl * 1.4, 0, 1)).astype(np.float32)
            self.T = np.maximum(self.T, np.clip(amp * cyl, 0, 1)).astype(np.float32)
            self.v += dt * 2.5 * amp * cyl
        self.vorticity_force(dt)
        # cooling and soot
        c = p.cool
        self.T = np.clip(self.T - dt * (c * self.T ** 3 + 0.15 * self.T), 0, 1)
        self.S = np.clip(self.S + dt * p.soot_rate * self.G * (1 - self.T) ** 2 * (0.2 + 0.8 * (1 - self.T)), 0, 1)
        # Absorbing layer at the top and sides: gas reaching the edge of the box leaves instead of piling up into a sheet.
        sponge = np.maximum(np.clip((yb - 0.9 * p.height) / (0.1 * p.height), 0, 1),
                            np.clip((np.maximum(np.abs(self.px), np.abs(self.pz)) - 0.9 * p.half) / (0.1 * p.half), 0, 1)).astype(np.float32)
        keep = (1 - np.clip(dt * 4.0 * sponge, 0, 1)).astype(np.float32)
        self.G *= keep; self.S *= keep; self.T *= keep
        self.project(dt, Q.astype(np.float32))
        if p.ground:
            self.v[:, :1, :] = np.maximum(self.v[:, :1, :], 0)
        self.time += dt

    def umax(self):
        return float(max(np.abs(self.u).max(), np.abs(self.v).max(), np.abs(self.w).max())) + 1e-3

    def run(self, log=True):
        p = self.p
        times = [p.tmax * (k / (p.frames - 1)) ** p.frame_pow for k in range(p.frames)]
        frames = []; k = 0; t0 = time.time(); steps = 0
        while k < len(times):
            if self.time + 1e-6 >= times[k]:
                frames.append(np.stack([self.G, self.T, self.S], axis=-1).copy()); k += 1; continue
            dt = min(p.dt_max, p.cfl * self.dx / self.umax(), times[k] - self.time)
            dt = max(dt, 1e-4)
            self.step(dt); steps += 1
            if log and steps % 20 == 0:
                print('t=%.3f steps=%d umax=%.2f  %.0fs' % (self.time, steps, self.umax(), time.time() - t0), file=sys.stderr)
        return np.array(times, np.float32), np.stack(frames)


# ---- quick look ----------------------------------------------------------------------------------
def flame_colour(t):
    t = np.clip(t, 0, 1)[..., None]
    stops = [(0.0, (0.0, 0.0, 0.0)), (0.3, (0.35, 0.05, 0.0)), (0.5, (0.86, 0.27, 0.03)), (0.65, (0.97, 0.5, 0.08)),
             (0.8, (1.0, 0.74, 0.24)), (0.92, (1.0, 0.9, 0.58)), (1.0, (1.0, 0.96, 0.86))]
    xs = np.array([s[0] for s in stops]); out = np.zeros(t.shape[:-1] + (3,), np.float32)
    for c in range(3):
        out[..., c] = np.interp(t[..., 0], xs, [s[1][c] for s in stops])
    return out


def render_side(frame, axis=2, sky=(0.5, 0.65, 0.9)):
    """Orthographic view along an axis: emission + soot absorption, front to back."""
    g, T, s = frame[..., 0], frame[..., 1], frame[..., 2]
    g = np.moveaxis(g, axis, 0); T = np.moveaxis(T, axis, 0); s = np.moveaxis(s, axis, 0)
    n = g.shape[0]
    acc = np.zeros(g.shape[1:] + (3,), np.float32); trans = np.ones(g.shape[1:], np.float32)
    for k in range(n):
        dens = np.clip(g[k], 0, 1) * 5.0 * 0.12
        a = 1 - np.exp(-dens * (1 + 2.5 * s[k]))
        emit = flame_colour(T[k]) * (0.2 + 3.0 * T[k] ** 3)[..., None]
        smoke = np.array([0.18, 0.17, 0.16], np.float32) * (0.5 + 0.5 * (1 - T[k]))[..., None]
        col = emit * (1 - s[k] * 0.8)[..., None] + smoke * (s[k] * (1 - T[k] * 0.5))[..., None]
        acc += (trans * a)[..., None] * col
        trans *= (1 - a)
    img = acc + trans[..., None] * np.array(sky, np.float32)
    img = 1 - np.exp(-img * 1.1)
    return np.clip(img, 0, 1)


def preview(times, frames, path, p, picks=None):
    from PIL import Image
    picks = picks or list(range(0, len(times), max(1, len(times) // 8)))[:8]
    tiles = []
    for k in picks:
        img = render_side(frames[k], axis=2)           # (x, y, 3): view along z
        img = np.transpose(img, (1, 0, 2))[::-1]         # y up
        tiles.append((img * 255).astype(np.uint8))
    scale = max(1, 256 // tiles[0].shape[1])
    tiles = [np.array(Image.fromarray(t).resize((t.shape[1] * scale, t.shape[0] * scale), Image.BICUBIC)) for t in tiles]
    h = max(t.shape[0] for t in tiles)
    row = np.concatenate([np.pad(t, ((h - t.shape[0], 0), (0, 0), (0, 0))) for t in tiles], axis=1)
    Image.fromarray(row).save(path)
    cell = p.half * 2 / p.nx
    for k in picks:
        g = frames[k][..., 0]; vol = float((g > 0.3).sum()) * cell ** 3
        ys = np.where((g > 0.1).any(axis=(0, 2)))[0]
        top = (ys[-1] + 1) * cell if len(ys) else 0
        print('  t=%.2f  radius_eq=%.2f  top=%.2f  maxT=%.2f  meanSoot=%.2f' % (times[k], (3 * vol / (4 * math.pi)) ** (1 / 3), top, frames[k][..., 1].max(), frames[k][..., 2][g > 0.3].mean() if (g > 0.3).any() else 0))
    print('preview', path, 'times', [round(float(times[k]), 2) for k in picks])


# ---- baking ---------------------------------------------------------------------------------------
BAKE = {
    'ground': dict(ground=True, res=56, frames=24, tmax=8.0, r0=0.5, expand=4.0, turb=4.0, buoy=3.2, cool=0.3, vort=0.8, seed=3, feed=0.55),
    # Second ground run: hotter push, more turbulence, less lift -> a wider, squatter, more ragged fireball (variety).
    'ground2': dict(ground=True, res=56, frames=24, tmax=8.0, r0=0.5, expand=5.0, turb=6.0, buoy=2.3, cool=0.3, vort=1.3, seed=11, feed=0.4),
    # Third: strong lift, weak push, steady feed -> a tall, narrow column of fire on a thick stem.
    'ground3': dict(ground=True, res=56, frames=24, tmax=8.0, r0=0.5, expand=2.5, turb=2.5, buoy=5.5, cool=0.3, vort=0.6, seed=17, feed=1.0, feed_time=3.0),
    # Fourth: weak lift, very turbulent -> a bushy, chaotic ball of fire and smoke with no clean stem.
    'ground4': dict(ground=True, res=56, frames=24, tmax=8.0, r0=0.5, expand=4.2, turb=6.0, buoy=1.5, cool=0.3, vort=1.4, seed=23, feed=0.15),
    'air':    dict(ground=False, res=56, frames=24, tmax=8.0, r0=0.5, expand=4.0, turb=4.0, buoy=1.0, cool=0.3, vort=0.8, seed=5, source_y=1.7),
    # Second air run: a calm, buoyant bubble rolls up into a rising ring cap trailing streamers.
    'air2':   dict(ground=False, res=56, frames=24, tmax=8.0, r0=0.5, expand=3.0, turb=0.6, buoy=3.0, cool=0.3, vort=1.0, seed=29, source_y=1.7),
    # Nuclear detonation (1.1): a big, very buoyant hot sphere just above the ground in a tall box; it rises, rolls into a
    # toroidal cap (vortex ring) and spreads under the stable layer at mid-height. The dust stem is drawn by the shader.
    'nuke':   dict(ground=True, res=56, frames=24, tmax=4.2, half=2.4, height=9.6, r0=1.1, source_y=1.15, expand=1.5, turb=0.6,
                   buoy=5.0, cool=0.08, vort=2.5, seed=41, feed=0.0, stable_at=0.5, stable=2.5, wobble=0.3, turb_ramp=1.2),
    # Second ground nuke ("Trinity"): a rougher, boiling fireball that rolls up more raggedly.
    'nuke2':  dict(ground=True, res=56, frames=24, tmax=4.2, half=2.4, height=9.6, r0=1.1, source_y=1.15, expand=1.5, turb=1.3,
                   buoy=5.0, cool=0.08, vort=2.0, seed=57, feed=0.0, stable_at=0.5, stable=2.5, wobble=0.55, turb_ramp=0.8),
    # Air burst: the same fireball with no ground under it (open box), rolling into a ring cap.
    'nukeair': dict(ground=False, res=56, frames=24, tmax=4.2, half=2.4, height=9.6, r0=1.1, source_y=2.6, expand=1.5, turb=0.6,
                   buoy=5.0, cool=0.08, vort=2.5, seed=43, feed=0.0, stable_at=0.55, stable=2.5, wobble=0.3, turb_ramp=1.2),
}
VARIANTS = ('ground', 'ground2', 'ground3', 'ground4', 'air', 'air2', 'nuke', 'nuke2', 'nukeair')


def make_params(variant, res=None, **over):
    cfg = dict(BAKE[variant]); cfg.update(over)
    n = res or cfg.pop('res'); cfg.pop('res', None)
    half = cfg.pop('half', 1.6)
    cell = 2 * half / n
    height = cfg.pop('height', 5.2 if variant.startswith('ground') else 5.0)
    ny = int(round(height / cell))
    p = Params(nx=n, nz=n, ny=ny, half=half, height=ny * cell, **cfg)
    return p


def frame_bounds(frames, p):
    """Occupied box of every frame in simulation units (x, y, z min/max) and the equivalent radius."""
    cell = 2 * p.half / p.nx
    out = []
    ypos = (np.arange(p.ny) + 0.5) * cell      # measured from the bottom of the box in both variants
    xpos = (np.arange(p.nx) + 0.5) * cell - p.half
    for f in frames:
        occ = (f[..., 0] > 0.04) | (f[..., 2] > 0.04)
        if not occ.any():
            out.append((0, 0, 0, 0, 0, 0, 0)); continue
        xs = np.where(occ.any(axis=(1, 2)))[0]; ys = np.where(occ.any(axis=(0, 2)))[0]; zs = np.where(occ.any(axis=(0, 1)))[0]
        vol = float((f[..., 0] > 0.3).sum()) * cell ** 3
        out.append((xpos[xs[0]] - cell, xpos[xs[-1]] + cell, ypos[ys[0]] - cell, ypos[ys[-1]] + cell, xpos[zs[0]] - cell, xpos[zs[-1]] + cell,
                    (3 * vol / (4 * math.pi)) ** (1 / 3)))
    return out


def bake_one(job):
    variant, outdir, res, quick = job
    if True:
        p = make_params(variant, res=res or (32 if quick else None))
        if quick: p.frames = 12
        sim = Sim(p)
        times, frames = sim.run(log=False)
        bounds = frame_bounds(frames, p)
        rpeak = max(b[6] for b in bounds)
        q = np.clip(np.rint(frames * 255), 0, 255).astype(np.uint8)         # (N, x, y, z, 3)
        rgba = np.concatenate([q, np.full(q.shape[:-1] + (1,), 255, np.uint8)], axis=-1)
        vol = np.ascontiguousarray(np.transpose(rgba, (0, 3, 2, 1, 4)))     # (N, z, y, x, 4): Unity x fastest, frames stacked in depth
        name = os.path.join(outdir, 'fire_%s.bin' % variant)
        vol.tofile(name)
        print('%s: %d frames %dx%dx%d  rpeak=%.2f  %.1f MB' % (variant, len(times), p.nx, p.ny, p.nz, rpeak, vol.nbytes / 1e6), file=sys.stderr)
        V = variant.capitalize()
        lines = []
        fl = lambda xs: ', '.join('%.4ff' % x for x in xs)
        lines += ['        public const int %sNx = %d, %sNy = %d, %sNz = %d, %sFrames = %d;' % (V, p.nx, V, p.ny, V, p.nz, V, len(times)),
                  '        public const float %sHalf = %.4ff, %sHeight = %.4ff, %sTMax = %.4ff, %sTimePower = %.4ff, %sRefRadius = %.4ff;' % (V, p.half, V, p.height, V, p.tmax, V, p.frame_pow, V, rpeak),
                  '        public const bool %sHasGround = %s;' % (V, 'true' if p.ground else 'false'),
                  '        public const float %sSourceY = %.4ff;' % (V, p.source_y),
                  '        // Largest horizontal half extent and height of the gas over the whole simulation (simulation units).',
                  '        public const float %sMaxXZ = %.4ff, %sMaxY = %.4ff;' % (V, max(max(abs(b[0]), abs(b[1]), abs(b[4]), abs(b[5])) for b in bounds), V, max(b[3] for b in bounds)),
                  '        // Occupied box per frame: x0, x1, y0, y1, z0, z1 (simulation units, y from the ground or centred), equivalent radius.',
                  '        public static readonly float[] %sBounds = { %s };' % (V, ', '.join(fl(b[:6]) for b in bounds) if False else ', '.join(', '.join('%.4ff' % x for x in b[:6]) for b in bounds)),
                  '        public static readonly float[] %sRadius = { %s };' % (V, fl([b[6] for b in bounds]))]
    return lines


def bake(outdir, table_path, res=None, quick=False):
    os.makedirs(outdir, exist_ok=True)
    lines = ['// <auto-generated> by Tools/firesim.py (%s). Do not edit.' % VERSION,
             'namespace VolumetricExplosionFX.Core', '{', '    public static class FireSimTable', '    {']
    import multiprocessing
    with multiprocessing.Pool(len(VARIANTS)) as pool:      # each run is independent and deterministic
        for part in pool.map(bake_one, [(v, outdir, res, quick) for v in VARIANTS]):
            lines += part
    lines += ['    }', '}']
    with open(table_path, 'w', newline=chr(10)) as fh:
        fh.write(chr(10).join(lines) + chr(10))
    print('table', table_path, file=sys.stderr)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--variant', choices=list(BAKE), default='ground')
    ap.add_argument('--res', type=int, default=32, help='cells across the width (x, z)')
    ap.add_argument('--out', default='work/firesim')
    ap.add_argument('--set', action='append', default=[], help='name=value parameter override')
    ap.add_argument('--bake', action='store_true')
    ap.add_argument('--quick', action='store_true')
    a = ap.parse_args()
    if a.bake:
        bake(a.out, 'Source/Core/FireSimTable.cs', quick=a.quick); return
    kw = {}
    for item in a.set:
        k, v = item.split('=')
        kw[k] = float(v) if '.' in v or 'e' in v else (v == 'true' if v in ('true', 'false') else int(v))
    p = make_params(a.variant, res=a.res, **kw)
    os.makedirs(a.out, exist_ok=True)
    t0 = time.time()
    sim = Sim(p)
    times, frames = sim.run()
    print('simulated in %.0fs' % (time.time() - t0), file=sys.stderr)
    np.savez_compressed(os.path.join(a.out, '%s_%d.npz' % (a.variant, p.nx)), times=times, frames=frames.astype(np.float16))
    preview(times, frames, os.path.join(a.out, '%s_%d.png' % (a.variant, p.nx)), p)


if __name__ == '__main__':
    main()
