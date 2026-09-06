# Watch Dog Energy Management — Customer Documentation Source

Version documented: 2.2.1  
Document edition: September 2026  
Prepared by: MicroBrain by Fadi Assi

This source accompanies the generated Word and PDF manuals. It documents only features present in Watch Dog Energy Management 2.2.1: the Dashboard, Grid and Generator meter views, optional Water view, meter details and consumption charts, Operations, tariff and monthly invoice publication settings, published invoice archive, invoice PDF/email, controller and meter commissioning, shops, users and access levels, system features, audit trail, backup/restore, and LAN browser access.

Terminology note: the current product uses **Grid** for the utility electricity supply. Earlier releases used “UMEME”; users should select Grid in the current interface.

Core operating sequence:

1. Administrator signs in and creates shops.
2. Administrator creates and tests each Modbus RTU or Modbus TCP controller/gateway.
3. Administrator creates meters, assigns each to a shop and selects Grid, Generator, or Water.
4. Commissioning is confirmed only after a successful communication test.
5. Operations staff monitor readings and communication status.
6. Billing staff save a meter-specific tariff and choose a monthly invoice publication day from 1–7.
7. Watch Dog creates invoices for the previous completed calendar month when it is running on the scheduled day.
8. Published invoices remain searchable and can be downloaded as PDF, printed, or emailed when SMTP is enabled.

Security and operational notes:

- Keep Administrator credentials private and give each person an individual account.
- Never publish SMTP app passwords in documentation or screenshots.
- The main Watch Dog PC alone runs the backend, database, and meter polling service.
- Other devices on the same LAN are browser clients and use `http://MAIN-PC-IP:5080`.
- Keep the main PC powered, awake, connected to the field network, and running Watch Dog.
- Create and verify backups regularly, especially before major configuration changes.

