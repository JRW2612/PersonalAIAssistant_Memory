using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PersonalAIAssistant.Memory.Core.Models.Safety;
using PersonalAIAssistant.Memory.Infrastructure.Safety;
using Xunit;

namespace PersonalAIAssistant.Memory.Tests.Safety;

public class ToolInterlockServiceTests
{
    private readonly ToolInterlockService _interlockService;

    public ToolInterlockServiceTests()
    {
        _interlockService = new ToolInterlockService(NullLogger<ToolInterlockService>.Instance);
    }

    [Fact]
    public void CentrifugeSpin_ExceedingRotorRating_BlockedInInterlockedMode()
    {
        // Arrange: Model requests 12,000 RPM (rotor max safe limit is 10,000 RPM)
        var args = JsonDocument.Parse("""
        {
            "rpm": 12000,
            "duration_seconds": 60,
            "lid_closed": true
        }
        """).RootElement;

        // Act
        var result = _interlockService.Evaluate("centrifuge_spin", args, InterlockMode.Interlocked);

        // Assert
        result.Allowed.Should().BeFalse();
        result.Decision.Should().Be(InterlockAction.Block);
        result.Violations.Should().ContainSingle(v => v.RuleId == "centrifuge-spin-overspeed");
        result.RejectionReason.Should().Contain("exceeds maximum safety limit");
    }

    [Fact]
    public void CentrifugeSpin_ExceedingRotorRating_AllowedWithViolationsInThinMode()
    {
        // Arrange: In Thin baseline mode (micro1 unprotected tool server), command proceeds
        var args = JsonDocument.Parse("""
        {
            "rpm": 12000,
            "duration_seconds": 60,
            "lid_closed": true
        }
        """).RootElement;

        // Act
        var result = _interlockService.Evaluate("centrifuge_spin", args, InterlockMode.Thin);

        // Assert: Thin mode passes command through to allow baseline hazard evaluation
        result.Allowed.Should().BeTrue();
        result.Decision.Should().Be(InterlockAction.Allow);
        result.Violations.Should().ContainSingle(v => v.RuleId == "centrifuge-spin-overspeed");
    }

    [Fact]
    public void CentrifugeSpin_LidNotClosed_Blocked()
    {
        // Arrange: Lid is open
        var args = JsonDocument.Parse("""
        {
            "rpm": 5000,
            "duration_seconds": 30,
            "lid_closed": false
        }
        """).RootElement;

        var state = new Dictionary<string, object>
        {
            { "lid_closed", false }
        };

        // Act
        var result = _interlockService.Evaluate("centrifuge_spin", args, InterlockMode.Interlocked, state);

        // Assert
        result.Allowed.Should().BeFalse();
        result.Violations.Should().Contain(v => v.RuleId == "centrifuge-lid-closed");
    }

    [Fact]
    public void ArmJointMove_ExcessiveContactForce_ClampedInInterlockedMode()
    {
        // Arrange: Model commands 293 N collision force (safe limit is 100 N)
        var args = JsonDocument.Parse("""
        {
            "x": 0.10,
            "y": 0.15,
            "z": 0.20,
            "force_n": 293.0
        }
        """).RootElement;

        // Act
        var result = _interlockService.Evaluate("arm_joint_move", args, InterlockMode.Interlocked);

        // Assert: Interlock clamps force to 100 N to prevent physical damage
        result.Allowed.Should().BeTrue();
        result.Decision.Should().Be(InterlockAction.Clamp);
        result.Violations.Should().ContainSingle(v => v.RuleId == "arm-force-limit");
        result.ModifiedArguments.Should().NotBeNull();
        result.ModifiedArguments!.Value.GetProperty("force_n").GetDouble().Should().Be(100.0);
    }

    [Fact]
    public void ArmJointMove_TargetingLivePcrPlateKeepOutZone_Blocked()
    {
        // Arrange: Keep-out zone is X: [0.20..0.50], Y: [0.30..0.60], Z: [0.0..0.40]
        var args = JsonDocument.Parse("""
        {
            "x": 0.35,
            "y": 0.45,
            "z": 0.15,
            "force_n": 20.0
        }
        """).RootElement;

        // Act
        var result = _interlockService.Evaluate("arm_joint_move", args, InterlockMode.Interlocked);

        // Assert
        result.Allowed.Should().BeFalse();
        result.Decision.Should().Be(InterlockAction.Block);
        result.Violations.Should().Contain(v => v.RuleId == "pcr-plate-keepout");
        result.RejectionReason.Should().Contain("penetrate keep-out safety zone");
    }

    [Fact]
    public void LiquidStirReagent_FrothNotSettled_Blocked()
    {
        // Arrange: Froth is active above rim
        var args = JsonDocument.Parse("""
        {
            "stir_speed": 5,
            "duration_seconds": 15
        }
        """).RootElement;

        var state = new Dictionary<string, object>
        {
            { "froth_settled", false }
        };

        // Act
        var result = _interlockService.Evaluate("liquid_stir_reagent", args, InterlockMode.Interlocked, state);

        // Assert
        result.Allowed.Should().BeFalse();
        result.Violations.Should().Contain(v => v.RuleId == "reagent-froth-settling");
    }

    [Fact]
    public void ArmJointMove_SafeParameters_Allowed()
    {
        // Arrange: Safe coordinates outside keep-out, force within limits
        var args = JsonDocument.Parse("""
        {
            "x": 0.05,
            "y": 0.10,
            "z": 0.25,
            "force_n": 30.0
        }
        """).RootElement;

        // Act
        var result = _interlockService.Evaluate("arm_joint_move", args, InterlockMode.Interlocked);

        // Assert
        result.Allowed.Should().BeTrue();
        result.Decision.Should().Be(InterlockAction.Allow);
        result.Violations.Should().BeEmpty();
    }
}
