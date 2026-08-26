window.quizizzoRealtime = (() => {
    const connections = new Map();

    async function notify(reference, method, value) {
        try {
            await reference.invokeMethodAsync(method, value);
        } catch {
            // The Blazor circuit may have been replaced while a callback was queued.
        }
    }

    async function bind(connection, role, partyId) {
        if (role === "Host") {
            await connection.invoke("ConnectHost", partyId);
        } else if (role === "Player") {
            await connection.invoke("ConnectPlayer");
        } else if (role === "Display") {
            await connection.invoke("ConnectDisplay");
        } else {
            throw new Error(`Unknown realtime role: ${role}`);
        }
    }

    async function start(key, reference, role, partyId) {
        await stop(key);

        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/party")
            .withAutomaticReconnect([0, 2000, 5000, 10000])
            .build();

        connection.on("StateChanged", async message => {
            if (role === "Display") {
                // Pairing changes the display's party group without replacing its durable identity.
                try {
                    await bind(connection, role, partyId);
                } catch {
                    await notify(reference, "SetConnectionStatus", "Reconnecting");
                }
            }

            await notify(reference, "HandleStateChanged", message.reason);
        });
        connection.onreconnecting(() => notify(reference, "SetConnectionStatus", "Reconnecting"));
        connection.onreconnected(async () => {
            try {
                await bind(connection, role, partyId);
                await notify(reference, "SetConnectionStatus", "Connected");
                await notify(reference, "HandleStateChanged", "Reconnected");
            } catch {
                await notify(reference, "SetConnectionStatus", "Disconnected");
            }
        });
        connection.onclose(() => notify(reference, "SetConnectionStatus", "Disconnected"));

        connections.set(key, connection);
        try {
            await connection.start();
            await bind(connection, role, partyId);
            await notify(reference, "SetConnectionStatus", "Connected");
        } catch (error) {
            connections.delete(key);
            await connection.stop();
            await notify(reference, "SetConnectionStatus", "Disconnected");
            throw error;
        }
    }

    async function stop(key) {
        const connection = connections.get(key);
        if (!connection) {
            return;
        }

        connections.delete(key);
        await connection.stop();
    }

    return { start, stop };
})();
