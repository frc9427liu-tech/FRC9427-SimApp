#!/usr/bin/env python3
"""v0.5.97 風格補強貼圖:擋土牆格子、苔蘚岩石、日/黃昏/夜天空漸層。
色票由參考截圖量測(天頂→地平線),全部程式繪製、原創。用法:python Tools/gen_textures3.py"""
import numpy as np
from PIL import Image, ImageFilter
from gen_textures import save, fbm, tile_noise, lerp, col, rgba_save, OUT
import os


def lattice_wall(n=512):
    """混凝土擋土牆:菱形肋條格 + 淡污漬。"""
    yy, xx = np.mgrid[0:n, 0:n] / n
    u = (xx + yy) * 6.0
    v = (xx - yy) * 6.0
    du = np.abs(u - np.round(u)); dv = np.abs(v - np.round(v))
    rib = np.clip(1.0 - np.minimum(du, dv) / 0.09, 0, 1)
    pocket = np.clip(np.minimum(du, dv) / 0.35, 0, 1)
    dirt = fbm(n, seed=311)
    base = lerp(np.tile(col('9a948a'), (n, n, 1)), np.tile(col('c2bcb0'), (n, n, 1)), rib * 0.8 + dirt * 0.25)
    base *= (0.72 + 0.28 * pocket)[..., None]
    save('retaining_wall_lattice', base)


def moss_rock(n=512):
    """白灰岩 + 頂面苔蘚(以噪聲高處當『朝上』近似)。"""
    h = fbm(n, ((1.6, 1.0), (2.4, 0.6), (3.2, 0.3)), seed=321)
    rock = lerp(np.tile(col('7d7f82'), (n, n, 1)), np.tile(col('d9dadc'), (n, n, 1)), h)
    moss_m = np.clip((h - 0.45) * 4.0, 0, 1) ** 0.7 * (0.7 + 0.3 * tile_noise(n, 2.4, 322))
    moss = lerp(np.tile(col('3f7a2c'), (n, n, 1)), np.tile(col('8fc84a'), (n, n, 1)), tile_noise(n, 2.2, 323))
    save('moss_rock', rock * (1 - moss_m[..., None]) + moss * moss_m[..., None])


def sky_strip(name, top, hz, h=256):
    t = np.linspace(0, 1, h)[:, None]
    a = col(top)[None, :] * (1 - t ** 0.8) + col(hz)[None, :] * (t ** 0.8)
    save(name, np.repeat(a[:, None, :], 4, axis=1))


if __name__ == '__main__':
    lattice_wall(); moss_rock()
    sky_strip('sky_day', '4a90de', '91b6d3')
    sky_strip('sky_dusk', '5f5996', 'd0885a')
    sky_strip('sky_night', '112a4f', '1e3552')
    print('done')
