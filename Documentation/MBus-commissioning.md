# M-Bus commissioning guide

Watchdog reads wired M-Bus meters (EN 13757-2/-3) through a **transparent M-Bus to Ethernet gateway**: the gateway passes raw M-Bus frames over a TCP socket. Electricity, heat and water meters are supported.

## 1. Prepare the gateway

- Give the gateway a fixed IP address on the network of the Watchdog server.
- Set its M-Bus side to the speed of your meters, usually **2400 baud, 8 data bits, even parity, 1 stop bit**.
- Turn on its **transparent TCP server** mode (sometimes called "raw TCP" or "TCP server") and note the port, often **10001**.
- Only one program can talk to the bus at a time. Close other M-Bus tools before Watchdog polls.

## 2. Add the gateway in Watchdog

1. Settings → Controller setup → **Add controller**.
2. Communication type: **M-Bus via Ethernet gateway (TCP)**.
3. Enter the gateway IP address and TCP port.
4. Polling interval: **300 seconds or more**. M-Bus takes about one second per meter, and battery-powered meters last longer with fewer reads. Readings are stored every 15 minutes in any case.
5. Response timeout: 1500–3000 ms. Retry count: 1–2.
6. Tick the commissioning confirmation and save.

## 3. Add the meters

On the controller's **meter channels** page, create one entry per meter. Primary addresses are numbered from the first address you enter.

Then open each meter's **Edit** page:

| Setting | What to enter |
|---|---|
| M-Bus primary address | The address set in the meter, 1–250. |
| M-Bus secondary address (ID) | Optional. The 8-digit ID printed on the meter. Use it when meters share a primary address, for example all at the factory value 0. When an ID is entered, the primary address is ignored. |
| Utility type | Electricity or heat meters bill the **energy total (kWh)**. Water bills the **volume total (m³)**. |
| Billing record | 0 = automatic: the first current total of energy or volume. Enter a record number only if the automatic choice is wrong. |
| Scaling factor | Normally **1**. M-Bus values already arrive in kWh or m³ (MWh, GJ, Wh and litres are converted automatically). |

Press **Read meter** to see everything the meter reports: ID, manufacturer, medium and every record with its value, unit, storage number and tariff. The row marked **Billed** is the value Watchdog will store and bill.

Tick **Commissioned and active** and save. The meter is read on the next poll.

## 4. What happens when a meter does not answer

Each meter is read on its own. If one meter is silent, it is marked **Failed**, and the other meters on the same gateway are still read. If the gateway itself cannot be reached, all its meters are marked **Failed** until the next poll.

Counter decreases go to **Counter review**, exactly as for Modbus and BACnet meters. Changing a meter's address, record or utility type starts a new counter on the next reading, so the change is never billed as a jump.

## Limits

- Wired M-Bus only. Wireless M-Bus (868 MHz radio) is not supported.
- Only the first telegram of each meter is read. Meters that split their data over several telegrams show "more records exist in a second telegram" on the Read meter page. The current totals are almost always in the first telegram.
- Encrypted M-Bus data (used by some wireless meters) is not supported.

## Matching the meter display (offset)

Every Modbus and M-Bus meter has an **Offset** on its Edit page. Watchdog shows *device value × scaling factor + offset*. If the PLC or gateway counts from 0 but the meter display already shows a total, type the number on the meter display into **Meter display now** and press **Calculate offset**, then save. Changing the offset starts a new counter on the next reading, so the difference is never billed.
