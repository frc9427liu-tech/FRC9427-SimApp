"""FRC9427 Sim - 機器人 CAD 匯入轉檔:C:\FRC\models\incoming 內的 .step/.stp/.glb/.gltf -> C:\FRC\models\team_<名稱>.glb
檔名尾巴可加 _yawNN(度)修正車頭方向,例如 1690_yaw90.step。模型規格:Y 向上、公尺、置中貼地、車頭 +Z(模擬器約定)。"""
import os, re, sys, glob, math, numpy as np, trimesh
SRC = r"C:\FRC\models\incoming"; DST = r"C:\FRC\models"; MAXF = 180000

def load_scene(path):
    ext = os.path.splitext(path)[1].lower()
    if ext in (".step", ".stp", ".iges", ".igs"):
        import cascadio
        tmp = path + ".tmp.glb"
        cascadio.step_to_glb(path, tmp, tol_linear=0.05, tol_angular=0.5, tol_relative=False, merge_primitives=True)
        sc = trimesh.load(tmp, force="scene"); os.remove(tmp); return sc, True
    return trimesh.load(path, force="scene"), False

def convert(path):
    name = os.path.splitext(os.path.basename(path))[0]
    m = re.search(r"_yaw(-?\d+)$", name); yaw = float(m.group(1)) if m else 0.0
    base = re.sub(r"_yaw-?\d+$", "", name); base = re.sub(r"[^A-Za-z0-9_\-]", "_", base)
    sc, zup = load_scene(path)
    meshes = [g for g in sc.dump(concatenate=False) if isinstance(g, trimesh.Trimesh)]
    if not meshes: print("no mesh:", path); return False
    mesh = trimesh.util.concatenate(meshes)
    if zup: mesh.apply_transform(trimesh.transformations.rotation_matrix(-math.pi / 2, [1, 0, 0]))   # Z-up -> Y-up
    ext = mesh.bounds[1] - mesh.bounds[0]
    if max(ext) > 20: mesh.apply_scale(0.001)   # 毫米 -> 公尺
    if yaw: mesh.apply_transform(trimesh.transformations.rotation_matrix(math.radians(yaw), [0, 1, 0]))
    b = mesh.bounds; mesh.apply_translation([-(b[0][0] + b[1][0]) / 2, -b[0][1], -(b[0][2] + b[1][2]) / 2])
    if len(mesh.faces) > MAXF:
        try:
            import fast_simplification
            v, f = fast_simplification.simplify(mesh.vertices, mesh.faces, target_reduction=1 - MAXF / len(mesh.faces))
            mesh = trimesh.Trimesh(v, f, process=False)
        except Exception as e: print("simplify skipped:", e)
    mesh.visual = trimesh.visual.ColorVisuals(mesh, vertex_colors=np.tile([185, 190, 200, 255], (len(mesh.vertices), 1)))
    out = os.path.join(DST, "team_" + base + ".glb"); mesh.export(out)
    print("OK", out, "faces", len(mesh.faces), "size(m)", np.round(mesh.bounds[1] - mesh.bounds[0], 2)); return True

if __name__ == "__main__":
    os.makedirs(SRC, exist_ok=True); n = 0
    for p in glob.glob(os.path.join(SRC, "*")):
        if os.path.splitext(p)[1].lower() in (".step", ".stp", ".iges", ".igs", ".glb", ".gltf"):
            try:
                if convert(p): os.replace(p, p + ".done"); n += 1
            except Exception as e: print("FAIL", p, e)
    print("converted", n)