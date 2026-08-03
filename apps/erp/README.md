# Codex ERP

A full-scope enterprise resource planning suite written entirely in Codex, covering financial accounting, supply chain, HR, manufacturing, project management, compliance, analytics, and industry-specific extensions. All monetary values are integer-encoded; every entity carries a company code for multi-company isolation.

## Modules

- **ErpTypes** -- Foundation records: Company, CostCenter, Plant, Warehouse, Chart of Accounts, Journal Entry, Vendor, Customer, Fixed Asset, Payment Terms, Tax, FX rates
- **FinGL** -- General ledger: journal posting with balance enforcement, period controls, trial balance, P&L aggregation
- **FinApAr** -- Vendor invoice lifecycle, 3-way match, AP payment runs, customer invoice lifecycle, cash receipts, aging buckets
- **FinTreasury** -- Bank accounts, cash position, probabilistic cash-flow forecasting, debt instruments, intercompany transfers, bank reconciliation
- **FinControlling** -- Cost-center budgets and variance, cost allocation, profit-center P&L, internal orders, product costing, CO-PA segments, activity-based costing
- **HrCore** -- Employee master with W-4 and direct deposit, payroll calculation (federal brackets, FICA, benefits, overtime), benefits enrollment, time and attendance, leave management, performance reviews, recruiting pipeline
- **SdSales** -- Credit management, condition-based pricing, sales quotes/orders, delivery with COGS posting, billing, returns/credit notes, sales analytics
- **MmProcurement** -- Material master, purchase requisition/order, goods receipt (GR/IR accrual), 3-way match, inventory valuation with FIFO
- **WmWarehouse** -- Bin-level structure, putaway strategies, pick strategies (FIFO/LIFO/FEFO/wave), cycle counting, cross-docking
- **PpPlanning** -- BOM explosion with scrap, work centers, routing, production orders, MRP, capacity loading, demand forecasting, shop-floor confirmations
- **QmQuality** -- Quality plans, inspection lots, nonconformance reports, vendor quality scoring, SPC control limits
- **PmMaintenance** -- Equipment master with criticality/MTBF, maintenance plans (preventive/CBM/predictive), work orders, failure codes, calibration
- **PsProject** -- WBS hierarchy, resource planning, time recording, milestone billing, earned value analysis
- **GrcCompliance** -- RBAC, SoD rules, risk register, audit management, policy attestations, incident tracking, compliance calendar
- **MdmMaster** -- Golden-record management, data quality rules, duplicate detection, cross-system sync, field-level change requests
- **BwAnalytics** -- Star-schema fact/dimension model, KPI definitions with RAG thresholds, executive dashboard, report definitions
- **IsBanking** -- Deposit accounts, loan origination, amortization, Basel III capital ratios, payment channels
- **IsHealthcare, IsInsurance, IsRealEstate, IsUtilities** -- Industry-specific extensions

## Completeness

65% -- Core financial stack (GL, AP, AR, Treasury, Controlling) is well-developed. HR, Sales, Procurement, Production Planning, Quality, Warehouse, Maintenance, and Project System all have complete data models and critical algorithms. What is absent: no persistence layer wiring beyond stubs, no HTTP/API surface, no `opening` entry point (the suite is a library of engines, not yet a runnable application). The five industry verticals vary in depth.

## Codex Conformance

Full -- All 22 source files are Codex. Backend persistence, API endpoints, and client UI are intended to be emitted through plugs; none have been authored for this app yet.
