// One action at a time on the game link (docs/Designs/Active/Apps/AssistStructure.md, primitives; assist-plan.md rule
// 3): the scope holds the action gate and, on entry, waits out the previous action's answer, in place of
// lock (actionGate) { Answered(k, AnswerMs); ... } written at each step.
#nullable enable
using System;
using System.Threading;

namespace Uoaix.Assist;

public sealed class ActionScope : IDisposable
{
    readonly object m_gate;
    bool m_held;

    public ActionScope(object gate, Action settle)
    {
        m_gate = gate;
        Monitor.Enter(m_gate);
        m_held = true;
        Settle(settle);
    }

    ActionScope(object gate)
    {
        m_gate = gate;
        m_held = true;
    }

    // The scope when the gate is free now, else null: a background loop that skips its turn rather than wait.
    public static ActionScope? TryEnter(object gate, Action settle)
    {
        if (!Monitor.TryEnter(gate))
            return null;
        ActionScope scope = new(gate);
        scope.Settle(settle);
        return scope;
    }

    void Settle(Action settle)
    {
        try
        {
            settle();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (!m_held)
            return;
        m_held = false;
        Monitor.Exit(m_gate);
    }
}
