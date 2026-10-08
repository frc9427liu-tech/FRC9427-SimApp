import java.lang.reflect.Method;
import java.util.ArrayList;
import java.util.List;

// Java agent:在「機器人 JVM 同一個程序」裡,用 Phoenix 6 官方 SimState 驅動全部 Talon FX(+swerve CANcoder),
// 讀 Talon 輸出電壓 → 同進程積分馬達物理 → 寫回轉子位置/速度。沒有網路延遲,等同別人(maple-sim/CTRE 範例)的做法。
// 以 JAVA_TOOL_OPTIONS=-javaagent:simagent.jar 注入;只在機器人 JVM 啟動(排除 gradle)。
// 物理模型與 Unity MechSim 對齊(Kraken X60;J、摩擦、限位、invert 鏡像)。
public class SimAgent {
    static final double RATIO = 26.09090909090909;          // swerve 轉向:轉子轉數 / 輪組轉數
    static final double R = 12.0 / 366.0, KT = 7.09 / 366.0, KE = 12.0 / (100.0 * 2 * Math.PI);   // Kraken X60

    static class M {
        int id, coder;                // coder<0:沒有 CANcoder
        double inertia, friction, minRot, maxRot;
        boolean invert;
        Object sim, coderSim;
        double pos, vel;              // 轉子 rev(HALSim 原始座標)、rad/s
        M(int id, int coder, double J, double fr, boolean inv, double minR, double maxR) {
            this.id = id; this.coder = coder; this.inertia = J; this.friction = fr; this.invert = inv;
            // 限位寫的是「使用者座標」;原始座標(Clockwise_Positive 反向)要鏡像,跟 Unity MechSim 一致
            this.minRot = inv ? -maxR : minR; this.maxRot = inv ? -minR : maxR;
        }
    }

    static List<M> table() {
        double INF = Double.POSITIVE_INFINITY;
        List<M> l = new ArrayList<>();
        // swerve 轉向 + CANcoder
        l.add(new M(46, 1, 0.002, 0.01, false, -INF, INF));
        l.add(new M(3, 2, 0.002, 0.01, false, -INF, INF));
        l.add(new M(7, 4, 0.002, 0.01, false, -INF, INF));
        l.add(new M(5, 3, 0.002, 0.01, false, -INF, INF));
        // swerve 驅動
        for (int id : new int[] {2, 4, 6, 8}) l.add(new M(id, -1, 0.002, 0.01, false, -INF, INF));
        // 飛輪
        for (int id : new int[] {9, 10, 11, 12}) l.add(new M(id, -1, 0.00155, 0.005, false, -INF, INF));
        // Hood(限位 1..45 度 → -0.0311..1.9158 rev)
        l.add(new M(15, -1, 0.00014192760700948003, 0.005, false, -0.031138888888888886, 1.915861111111111));
        // 滾輪、輸送帶、Tigger、手臂
        l.add(new M(41, -1, 0.00045, 0.005, false, -INF, INF));
        l.add(new M(45, -1, 0.00045, 0.005, false, -INF, INF));
        l.add(new M(31, -1, 0.00365, 0.005, true, -INF, INF));
        l.add(new M(13, -1, 0.00045, 0.005, true, -INF, INF));
        l.add(new M(14, -1, 0.00045, 0.005, true, -INF, INF));
        l.add(new M(30, -1, 0.00008024187499999999, 0.005, true, -0.5213264277655786, 13.554487121905042));
        return l;
    }

    public static void premain(String args) {
        String cmd = System.getProperty("sun.java.command", "");
        System.out.println("[simagent] premain cmd=" + (cmd.length() > 160 ? cmd.substring(0, 160) : cmd));
        String lc = cmd.toLowerCase();
        if (lc.contains("gradle")) return;   // gradle wrapper / daemon 不是機器人
        if (!(lc.contains("frc.robot") || lc.endsWith(".jar") || lc.contains("robot"))) return;
        Thread t = new Thread(SimAgent::run, "simagent");
        t.setDaemon(true);
        t.start();
    }

    static void log(String s) { System.out.println("[simagent] " + s); }

    static void run() {
        try {
            Thread.sleep(8000);   // 等機器人 code 建好自己的裝置
            ClassLoader cl = ClassLoader.getSystemClassLoader();
            Class<?> talonC = Class.forName("com.ctre.phoenix6.hardware.TalonFX", true, cl);
            Class<?> coderC = Class.forName("com.ctre.phoenix6.hardware.CANcoder", true, cl);
            List<M> ms = table();
            Method getV = null, setPos = null, setVel = null, setSupply = null, cPos = null, cVel = null;
            for (M m : ms) {
                Object talon = talonC.getConstructor(int.class).newInstance(m.id);
                m.sim = talonC.getMethod("getSimState").invoke(talon);
                if (m.coder >= 0) {
                    Object coder = coderC.getConstructor(int.class).newInstance(m.coder);
                    m.coderSim = coderC.getMethod("getSimState").invoke(coder);
                }
                if (getV == null) {
                    Class<?> ts = m.sim.getClass();
                    getV = ts.getMethod("getMotorVoltage");
                    setPos = ts.getMethod("setRawRotorPosition", double.class);
                    setVel = ts.getMethod("setRotorVelocity", double.class);
                    setSupply = ts.getMethod("setSupplyVoltage", double.class);
                }
                if (m.coderSim != null && cPos == null) {
                    Class<?> cs = m.coderSim.getClass();
                    cPos = cs.getMethod("setRawPosition", double.class);
                    cVel = cs.getMethod("setVelocity", double.class);
                }
            }
            log("started: driving " + ms.size() + " motors in-process");
            long last = System.nanoTime(), nextLog = last;
            double maxV = 0;
            while (true) {
                long now = System.nanoTime();
                double dt = Math.min((now - last) / 1e9, 0.01);
                last = now;
                for (M m : ms) {
                    double v = (Double) getV.invoke(m.sim);
                    maxV = Math.max(maxV, Math.abs(v));
                    double drive = KT * v / R;
                    double torque = drive - (KT * KE / R) * m.vel;
                    double fr = (Math.abs(m.vel) < 1e-3 && Math.abs(torque) < m.friction) ? -torque : -Math.signum(m.vel) * m.friction;
                    m.vel += ((torque + fr) / m.inertia) * dt;
                    m.pos += m.vel / (2 * Math.PI) * dt;
                    if (m.pos < m.minRot) { m.pos = m.minRot; if (m.vel < 0) m.vel = 0; }
                    if (m.pos > m.maxRot) { m.pos = m.maxRot; if (m.vel > 0) m.vel = 0; }
                    setSupply.invoke(m.sim, 12.0);
                    setPos.invoke(m.sim, m.pos);
                    setVel.invoke(m.sim, m.vel / (2 * Math.PI));
                    if (m.coderSim != null) {
                        cPos.invoke(m.coderSim, m.pos / RATIO);
                        cVel.invoke(m.coderSim, m.vel / (2 * Math.PI) / RATIO);
                    }
                }
                if (now > nextLog) { nextLog = now + 2_000_000_000L; log(String.format("steer46 pos=%.3f rev | fly9 vel=%.1f rad/s | max|V|=%.2f", ms.get(0).pos, ms.get(8).vel, maxV)); }
                Thread.sleep(2);
            }
        } catch (Throwable e) {
            log("failed: " + e);
            e.printStackTrace();
        }
    }
}
