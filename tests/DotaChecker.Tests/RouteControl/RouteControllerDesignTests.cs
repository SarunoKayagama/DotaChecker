using DotaChecker.Core.RouteControl;

namespace DotaChecker.Tests.RouteControl;

/// <summary>
/// Мок-тесты будущего Route Controller: проверяем контракт предусловий и плана.
/// Реальных изменений сетевой конфигурации нет и быть не должно.
/// </summary>
public sealed class RouteControllerDesignTests
{
    [Fact]
    public void PlanRequiresConfirmationAndElevation()
    {
        var controller = new MockRouteController();
        var plan = controller.BuildPlan(new RouteControlRequest("warp", "key", false));

        plan.RequiresUserConfirmation.Should().BeTrue();
        plan.RequiresElevation.Should().BeTrue();
        plan.Steps.Should().NotBeEmpty();
        plan.RollbackSteps.Should().NotBeEmpty();
        plan.Explanation.Should().Contain("не выполняются");
    }

    [Fact]
    public void PreconditionsBlockWhenUserDidNotConfirm()
    {
        var controller = new MockRouteController();

        var preconditions = controller.CheckPreconditions();

        preconditions.CanProceed.Should().BeFalse();
        preconditions.RequiresUserConfirmation.Should().BeTrue();
    }

    private sealed class MockRouteController : IRouteController
    {
        public RouteControlPrecondition CheckPreconditions() =>
            new(false, true, true, new[] { "Подтверждение пользователя не получено." });

        public RouteControlPlan BuildPlan(RouteControlRequest request) =>
            new(true, true,
                new[] { "Проверить состояние туннеля", "Запросить подтверждение", "Применить", "Проверить результат" },
                new[] { "Восстановить исходный интерфейс", "Проверить маршрутизацию" },
                "План для будущего контроллера; команды не выполняются тестом.");

        public RouteControlSnapshot ReadState() =>
            new("Direct", "eth0", false, false, new[] { "Только mock-состояние." });
    }
}
