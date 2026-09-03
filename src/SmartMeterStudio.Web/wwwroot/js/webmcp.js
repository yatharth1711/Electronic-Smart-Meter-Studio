(() => {
    const context = document.modelContext;
    if (!context?.registerTool) return;

    const register = (tool) => {
        try { Promise.resolve(context.registerTool(tool)).catch(() => {}); }
        catch { /* Browser does not support this draft API completely. */ }
    };

    register({
        name: "list_virtual_meters",
        title: "List virtual meters",
        description: "Read the current SmartMeter Studio fleet and live meter state.",
        inputSchema: { type: "object", properties: {}, additionalProperties: false },
        annotations: { readOnlyHint: true, untrustedContentHint: false },
        execute: async () => {
            const response = await fetch("/api/meters");
            if (!response.ok) throw new Error("Unable to read the virtual-meter fleet.");
            return await response.json();
        }
    });

    register({
        name: "inject_meter_fault",
        title: "Inject a meter fault",
        description: "Inject a controlled fault into one virtual smart meter for a simulated duration.",
        inputSchema: {
            type: "object",
            properties: {
                meterId: { type: "string", description: "Virtual meter ID, for example MTR-0001." },
                type: { type: "string", enum: ["VoltageDip", "VoltageSwell", "PhaseLoss", "LowPowerFactor", "HarmonicBurst", "ReverseEnergy", "CommunicationDropout", "ClockDrift"] },
                durationSeconds: { type: "integer", minimum: 1, maximum: 86400 }
            },
            required: ["meterId", "type", "durationSeconds"],
            additionalProperties: false
        },
        annotations: { readOnlyHint: false, untrustedContentHint: false },
        execute: async ({ meterId, type, durationSeconds }) => {
            const response = await fetch(`/api/meters/${encodeURIComponent(meterId)}/faults`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ type, durationSeconds })
            });
            if (!response.ok) throw new Error(`Fault injection failed with status ${response.status}.`);
            return await response.json();
        }
    });
})();
