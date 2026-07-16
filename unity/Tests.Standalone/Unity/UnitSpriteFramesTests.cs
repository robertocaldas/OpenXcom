using OpenXcom.Unity.Rendering;
using Xunit;

namespace OpenXcom.Core.Tests.Unity
{
    public class UnitSpriteFramesTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(7)]
        public void BodyPartFrame_Standing_IsBasePlusDirection(int direction)
        {
            int frame = UnitSpriteFrames.BodyPartFrame(
                UnitSpriteFrames.LegsStandBase, UnitSpriteFrames.LegsWalkBase, direction, walkPhase: -1);
            Assert.Equal(UnitSpriteFrames.LegsStandBase + direction, frame);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(3, 5)]
        [InlineData(7, 7)]
        public void BodyPartFrame_Walking_IsBasePlus24TimesDirectionPlusWalkPhase(int direction, int walkPhase)
        {
            int frame = UnitSpriteFrames.BodyPartFrame(
                UnitSpriteFrames.RightArmStandBase, UnitSpriteFrames.RightArmWalkBase, direction, walkPhase);
            Assert.Equal(UnitSpriteFrames.RightArmWalkBase + 24 * direction + walkPhase, frame);
        }

        [Fact]
        public void BodyPartFrame_WalkPhaseIsTakenMod8()
        {
            // getWalkingPhase() always returns _walkPhase % 8, so a caller
            // passing an out-of-range value (defensive) still lands correctly.
            int frame = UnitSpriteFrames.BodyPartFrame(0, 100, direction: 0, walkPhase: 9);
            Assert.Equal(100 + 1, frame); // 9 % 8 == 1
        }

        [Fact]
        public void TorsoFrame_IsAlwaysBasePlusDirection_RegardlessOfCallerIntent()
        {
            Assert.Equal(UnitSpriteFrames.TorsoBase + 5, UnitSpriteFrames.TorsoFrame(5));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void DeathFrame_IsDieBasePlusPhase_NoDirectionParameter(int phase)
        {
            Assert.Equal(UnitSpriteFrames.DieBase + phase, UnitSpriteFrames.DeathFrame(phase));
        }

        [Fact]
        public void HeldRightArmFrame_OneHanded_AlwaysUsesTheStaticOneHandedPose()
        {
            Assert.Equal(UnitSpriteFrames.RightArmOneHanded + 3,
                UnitSpriteFrames.HeldRightArmFrame(twoHanded: false, isAiming: false, direction: 3));
            // one-handed weapons never get a special aiming arm pose (UnitSprite.cpp:498)
            Assert.Equal(UnitSpriteFrames.RightArmOneHanded + 3,
                UnitSpriteFrames.HeldRightArmFrame(twoHanded: false, isAiming: true, direction: 3));
        }

        [Fact]
        public void HeldRightArmFrame_TwoHanded_SwitchesToTheAimPoseWhileAiming()
        {
            Assert.Equal(UnitSpriteFrames.RightArmTwoHandedCarry + 2,
                UnitSpriteFrames.HeldRightArmFrame(twoHanded: true, isAiming: false, direction: 2));
            Assert.Equal(UnitSpriteFrames.RightArmTwoHandedAim + 2,
                UnitSpriteFrames.HeldRightArmFrame(twoHanded: true, isAiming: true, direction: 2));
        }

        [Fact]
        public void HeldLeftArmFrame_IsAlwaysTheStaticTwoHandedCarryPose()
        {
            Assert.Equal(UnitSpriteFrames.LeftArmTwoHanded + 6, UnitSpriteFrames.HeldLeftArmFrame(6));
        }

        [Fact]
        public void HeldItemFrame_NotAiming_IsHandSpritePlusPlainDirection()
        {
            Assert.Equal(104 + 5, UnitSpriteFrames.HeldItemFrame(handSprite: 104, twoHanded: false, isAiming: false, direction: 5));
            Assert.Equal(0 + 5, UnitSpriteFrames.HeldItemFrame(handSprite: 0, twoHanded: true, isAiming: false, direction: 5));
        }

        [Fact]
        public void HeldItemFrame_AimingTwoHanded_FlipsDirectionByAQuarterTurn()
        {
            // (direction + 2) % 8 - UnitSprite.cpp's own aiming-pose quirk.
            Assert.Equal(0 + ((5 + 2) % 8), UnitSpriteFrames.HeldItemFrame(handSprite: 0, twoHanded: true, isAiming: true, direction: 5));
        }

        [Fact]
        public void HeldItemFrame_AimingOneHanded_DoesNotFlipDirection()
        {
            // the +2 flip is two-handed-only (UnitSprite.cpp:498 has no such branch for rarm1H).
            Assert.Equal(104 + 5, UnitSpriteFrames.HeldItemFrame(handSprite: 104, twoHanded: false, isAiming: true, direction: 5));
        }

        [Fact]
        public void AimOffsets_HaveEightEntriesMatchingUnitSpriteCppLiterals()
        {
            Assert.Equal(new[] { 8, 10, 7, 4, -9, -11, -7, -3 }, UnitSpriteFrames.AimOffsetX);
            Assert.Equal(new[] { -6, -3, 0, 2, 0, -4, -7, -9 }, UnitSpriteFrames.AimOffsetY);
        }
    }
}
