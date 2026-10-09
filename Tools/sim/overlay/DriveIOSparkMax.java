package frc.robot.subsystems.drive;

import com.ctre.phoenix6.controls.VoltageOut;
import com.ctre.phoenix6.hardware.TalonFX;
import frc.robot.Constants.DriveConstants;

/**
 * 模擬器專用替身(由 FRC9427 模擬器在「工作副本」裡覆蓋,不會動到你原本的專案):
 * 這台電腦的 Windows「應用程式控制原則」會擋掉 REVLibDriver.dll,SparkMax 在模擬裡載不起來,
 * 所以底盤四顆馬達改用 Phoenix TalonFX 代替(同 CAN ID),由模擬器的物理 agent 驅動。
 * 出力換算與真實版一致:duty * kNominalVolts。正的 = 往前(模擬器那邊決定哪個方向是車頭)。
 */
public class DriveIOSparkMax implements DriveIO {
  private final TalonFX m_leftLeader = new TalonFX(DriveConstants.kLeftLeaderCanId);
  private final TalonFX m_leftFollower = new TalonFX(DriveConstants.kLeftFollowerCanId);
  private final TalonFX m_rightLeader = new TalonFX(DriveConstants.kRightLeaderCanId);
  private final TalonFX m_rightFollower = new TalonFX(DriveConstants.kRightFollowerCanId);
  private final VoltageOut m_req = new VoltageOut(0.0);
  private double m_left;
  private double m_right;

  public DriveIOSparkMax() {}

  @Override
  public void configure() {}

  @Override
  public void setDutyCycle(double left, double right) {
    m_left = left;
    m_right = right;
    double l = left * DriveConstants.kNominalVolts;
    double r = right * DriveConstants.kNominalVolts;
    m_leftLeader.setControl(m_req.withOutput(l));
    m_leftFollower.setControl(m_req.withOutput(l));
    m_rightLeader.setControl(m_req.withOutput(r));
    m_rightFollower.setControl(m_req.withOutput(r));
  }

  @Override
  public double getLeftOutput() {
    return m_left;
  }

  @Override
  public double getRightOutput() {
    return m_right;
  }

  @Override
  public double getLeftCurrent() {
    return 0.0;
  }

  @Override
  public double getRightCurrent() {
    return 0.0;
  }
}