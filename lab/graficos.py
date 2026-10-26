# Genera las figuras del informe a partir de los JSON de resultados.
import json, os
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import FancyBboxPatch, FancyArrowPatch

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RES = os.path.join(BASE, "resultados")
OUT = os.path.join(BASE, "graficos")
os.makedirs(OUT, exist_ok=True)

A, B, G, INK, MUTE = "#C0392B", "#1E7F5C", "#7F8C8D", "#1F2933", "#6B7280"
plt.rcParams.update({"font.family": "Calibri", "font.size": 11, "axes.spines.top": False,
                     "axes.spines.right": False, "axes.edgecolor": "#9CA3AF", "axes.labelcolor": INK,
                     "xtick.color": INK, "ytick.color": INK, "axes.titleweight": "bold", "axes.titlesize": 12})

dep = json.load(open(os.path.join(RES, "resultados_deploy.json")))["deploy"]
rb = json.load(open(os.path.join(RES, "resultados_rollback.json")))["rollback"]
esc = json.load(open(os.path.join(RES, "resultados_escala.json")))["escala"]

def save(fig, name):
    fig.tight_layout(); fig.savefig(os.path.join(OUT, name), dpi=200); plt.close(fig)

# Fig 1: tiempo de inactividad por actualizacion
fig, ax = plt.subplots(figsize=(7, 3.4))
reps = [e["rep"] for e in dep["tradicional"]]
w = 0.38
ax.bar([r - w/2 for r in reps], [e["downtimeMs"]/1000 for e in dep["tradicional"]], w, color=A, label="Tradicional (detener-copiar-iniciar)")
ax.bar([r + w/2 for r in reps], [e["downtimeMs"]/1000 for e in dep["blueGreen"]], w, color=B, label="Blue-green con proxy inverso")
for r in reps: ax.text(r + w/2, 0.03, "0", ha="center", color=B, fontsize=9, fontweight="bold")
ax.set_xticks(reps); ax.set_xlabel("Actualización n.º"); ax.set_ylabel("Inactividad (s)")
ax.set_title("Inactividad observada en 10 actualizaciones bajo carga")
ax.legend(frameon=False, loc="upper center", bbox_to_anchor=(0.5, -0.22), ncol=2); ax.set_ylim(0, 2.2)
save(fig, "fig1_inactividad.png")

# Fig 2: MTTR (escala logaritmica)
fig, ax = plt.subplots(figsize=(6, 3.2))
import statistics as st
vals = [st.mean([e["mttrMs"] for e in rb["tradicional"]]), st.mean([e["mttrMs"] for e in rb["blueGreen"]])]
bars = ax.barh(["Tradicional\n(re-publicar v1)", "Blue-green\n(devolver tráfico)"], vals, color=[A, B])
ax.set_xscale("log"); ax.set_xlabel("Tiempo de reversión tras la detección (ms, escala log)")
for b, v in zip(bars, vals): ax.text(v * 1.15, b.get_y() + b.get_height()/2, f"{v:,.1f} ms".replace(",", "."), va="center", fontweight="bold", color=INK)
ax.set_xlim(0.5, 20000); ax.set_title("Reversión ante una versión defectuosa (media de 5)")
save(fig, "fig2_reversion.png")

# Fig 3: capacidad fija
fig, ax = plt.subplots(figsize=(6.4, 3.4))
f = esc["fijo"]; xs = [str(e["instancias"]) for e in f]
bars = ax.bar(xs, [e["rps"] for e in f], color=[G, "#3D8F73", B], width=0.55)
for b, e in zip(bars, f): ax.text(b.get_x() + b.get_width()/2, e["rps"] + 60, f"{e['rps']:.0f}", ha="center", fontweight="bold")
ax.set_xlabel("Instancias detrás del proxy (2 procesadores lógicos c/u)"); ax.set_ylabel("Solicitudes por segundo")
ax2 = ax.twinx(); ax2.plot(xs, [e["p95"] for e in f], "o-", color=A); ax2.set_ylabel("Latencia p95 (ms)", color=A)
ax2.tick_params(axis="y", colors=A); ax2.set_ylim(0, 110); ax2.spines["top"].set_visible(False)
ax.set_ylim(0, 4600); ax.set_title("Escalado horizontal con 64 usuarios concurrentes")
save(fig, "fig3_escalado.png")

# Fig 4: rampa con y sin autoescalado
fig, axs = plt.subplots(2, 1, figsize=(7, 4.8), sharex=True)
for k, col, lab in [("sinAuto", A, "1 instancia fija"), ("conAuto", B, "Autoescalado (SLO p95 ≤ 30 ms)")]:
    s = esc[k]["serie"]
    axs[0].plot([p["seg"] for p in s], [p["rps"] for p in s], color=col, label=lab, lw=1.8)
    axs[1].plot([p["seg"] for p in s], [p["p95"] for p in s], color=col, lw=1.8)
for ev in esc["conAuto"]["eventos"]:
    for a in axs: a.axvline(ev["seg"], color=B, ls=":", lw=1)
    axs[0].text(ev["seg"] + 0.5, 4300, ev["accion"].replace("escala a ", "→ "), color=B, fontsize=8.5)
for i, u in enumerate([4, 8, 16, 32, 64]): axs[1].text(i*12 + 6, 103, f"{u} usuarios", ha="center", fontsize=8.5, color=MUTE)
axs[1].axhline(30, color=MUTE, ls="--", lw=1); axs[1].text(1, 33, "SLO 30 ms", color=MUTE, fontsize=8.5)
axs[0].set_ylabel("Solicitudes/s"); axs[1].set_ylabel("p95 (ms)"); axs[1].set_xlabel("Tiempo de la prueba (s)")
axs[0].set_ylim(0, 4700); axs[1].set_ylim(0, 112)
axs[0].legend(frameon=False, loc="upper left"); axs[0].set_title("Carga creciente: capacidad fija vs. autoescalado")
save(fig, "fig4_autoescalado.png")

# Fig 5: carga WASM
fig, ax = plt.subplots(figsize=(6, 2.8))
mb = [8731521/1e6, 3348522/1e6, 2600986/1e6]
bars = ax.barh(["Sin comprimir", "Gzip", "Brotli"], mb, color=[G, "#D68910", B])
for b, v in zip(bars, mb): ax.text(v + 0.1, b.get_y() + b.get_height()/2, f"{v:.2f} MB".replace(".", ","), va="center", fontweight="bold")
ax.invert_yaxis(); ax.set_xlim(0, 10); ax.set_xlabel("Tamaño de los 39 recursos de _framework (MB)")
ax.set_title("Descarga inicial de Blazor WebAssembly según compresión")
save(fig, "fig5_wasm.png")

# Fig 6: arquitectura blue-green
fig, ax = plt.subplots(figsize=(7.4, 3.6)); ax.axis("off"); ax.set_xlim(0, 10); ax.set_ylim(0, 5)
def box(x, y, w, h, txt, fc, tc="white"):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.05,rounding_size=0.15", fc=fc, ec="none"))
    ax.text(x + w/2, y + h/2, txt, ha="center", va="center", color=tc, fontsize=10, fontweight="bold")
def arrow(p, q, c=INK, ls="-"):
    ax.add_patch(FancyArrowPatch(p, q, arrowstyle="-|>", mutation_scale=14, color=c, lw=1.6, ls=ls))
box(0.1, 2.0, 1.6, 1.0, "Usuarios\n(carga Node.js)", G)
box(2.5, 2.0, 1.9, 1.0, "Proxy inverso\n(round-robin)", INK)
box(5.4, 3.3, 2.0, 1.0, "BLUE  v1\n/healthz  200", "#1F5FA8")
box(5.4, 0.7, 2.0, 1.0, "GREEN  v2\n/healthz  200", B)
box(8.2, 2.0, 1.7, 1.0, "Pipeline\nCI/CD", "#7D3C98")
arrow((1.7, 2.5), (2.5, 2.5)); arrow((4.4, 2.7), (5.4, 3.7), "#1F5FA8", "--"); arrow((4.4, 2.3), (5.4, 1.2), B)
arrow((8.2, 2.3), (7.4, 1.3), "#7D3C98");
ax.text(4.1, 3.85, "tráfico previo\n(drenado 2 s)", fontsize=8.5, color="#1F5FA8", ha="center")
ax.text(4.0, 0.95, "tráfico nuevo tras\nhealth check + smoke test", fontsize=8.5, color=B, ha="center")
ax.text(8.45, 1.15, "1. publica en\nslot inactivo", fontsize=8.5, color="#7D3C98")
ax.set_title("Arquitectura blue-green implementada en el laboratorio", color=INK)
save(fig, "fig6_arquitectura.png")
print("ok", os.listdir(OUT))
