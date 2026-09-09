document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('[data-bacnet]').forEach(button => button.addEventListener('click', async () => {
        const form = button.closest('form'), result = document.getElementById('bacnet-result');
        const buttons = form.querySelectorAll('[data-bacnet]');
        buttons.forEach(b => b.disabled = true); result.textContent = 'Waiting for BACnet responses…';
        try {
            const data = new FormData(form); data.set('operation', button.dataset.bacnet);
            const url = new URL(location.href); url.searchParams.set('handler', 'Bacnet');
            const response = await fetch(url, { method: 'POST', body: data });
            if (!response.ok) throw new Error('BACnet request failed. Check your session and settings.');
            const reply = await response.json();
            if (reply.error) throw new Error(reply.error);
            if (reply.device) result.textContent = `BACnet connection successful — ${reply.device.name}, Device Instance ${reply.device.instance}, ${reply.device.ip}:${reply.device.port}`;
            if (reply.devices) {
                result.textContent = `${reply.devices.length} devices discovered. Select one to populate the form, then save.`;
                const container = document.getElementById('bacnet-devices'); container.replaceChildren();
                const table = document.createElement('table'), header = table.createTHead().insertRow();
                ['Device Name','Device Instance','IP Address','Port','Vendor ID','Model','Status',''].forEach(v => { const th=document.createElement('th'); th.textContent=v; header.append(th); });
                const body=table.createTBody();
                reply.devices.forEach(d => {
                    const row=body.insertRow(); [d.name,d.instance,d.ip,d.port,d.vendor,d.model,d.status].forEach(v => row.insertCell().textContent=v);
                    const select=document.createElement('button'); select.type='button'; select.textContent='Select';
                    select.addEventListener('click', () => {
                        [['IpAddress',d.ip],['BacnetUdpPort',d.port],['BacnetDeviceInstance',d.instance]].forEach(([k,v]) => form.elements[`Input.${k}`].value=v);
                        if(d.name) form.elements['Input.Name'].value=d.name;
                        result.textContent='Device selected. Review the settings and save.';
                    }); row.insertCell().append(select);
                }); container.append(table);
            }
        } catch(error) { result.textContent=error.message; }
        finally { buttons.forEach(b => b.disabled=false); }
    }));
});
