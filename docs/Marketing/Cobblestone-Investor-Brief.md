# Cobblestone & Codex

**Readable software. Explicit permissions. A compiler that rebuilds itself.**

Investor briefing | September 2026

Cobblestone is a computing platform for software that people can inspect, adapt and run independently. Codex is the platform's programming language and compiler. Together, the platform and language connect readable source, machine-checked rules and the system that executes the resulting programs.

The aim is greater control over computing: useful software with less setup, clearer permissions and stronger evidence about program behavior, from browser-based creation tools to devices running directly on hardware.

## What the platform does

Codex translates programs into executable software or other programming languages through modular output adapters called plugs. A shared language and compiler can support browser tools, native applications and embedded systems. Target support varies; new output formats can be added without redesigning the compiler core.

Cobblestone supplies the surrounding foundation: execution, operating-system services, identity, networking, libraries and development tools. The project includes a compiler capable of running directly on supported hardware and rebuilding from its own source, without a separate operating system or installed developer environment underneath the compiler.

Cobblestone also works through familiar environments. The same project supports browser-based compilation and integration with existing applications. Adoption can begin with a useful tool rather than requiring a customer to replace an entire computing environment.

## What makes the approach distinctive

**The compiler can sustain its own environment.** Recorded release tests demonstrate byte-identical compiler rebuilds. A self-contained build foundation creates possibilities for offline development, long-lived devices and installations where dependence on a remote service is undesirable.

**Permissions and resource rules are part of the program.** Codex distinguishes calculation from operations such as network or device access. Ownership rules and declared constraints allow the compiler to reject defined classes of unsafe behavior before execution. Selected program properties can also carry machine-checked proofs. These mechanisms provide enforceable boundaries within their supported scope.

**Trust includes checks on the compiler itself.** The project has exercised an independent compiler-rebuild path and a separate type-checking witness. Deliberately altered compiler tests have demonstrated that verification mechanisms detect the tested alterations. Rebuilding consistently and checking independently address different questions about trust.

**Human meaning stays close to executable code.** Codex organizes source as chapters combining explanation with formal definitions. For supported forms, compiler warnings identify mismatches between prose declarations and code. Human inspection and machine checking both matter, including for software produced with AI assistance.

The distinguishing proposition is the combination: readable programs, explicit authority, independent verification and a computing foundation capable of rebuilding itself.

<!-- PAGE BREAK -->

# From a technical foundation to useful products

**Start with an application people can use; grow into the infrastructure behind it.**

## What exists today

Working technology includes the language and compiler, a bare-metal runtime, browser tools, output plugs and application demonstrations. Project records document self-rebuilds, a USB-booted desktop, networking demonstrations and independent verification tests. Hardware coverage and production readiness remain specific to each target.

A current preview is **Cobblestone ModBuilder**. Players can select Valheim features, inspect and edit source, compile a mod DLL locally in the browser, and deploy through a local helper. Compilation also works from a downloaded page, without Git, Visual Studio or a developer SDK. Broader gameplay acceptance remains underway. The demonstration makes software creation approachable and keeps code available for inspection.

## Where the platform could go

**Creation tools and developer products.** Browser-based builders could serve specialist communities, business applications and education. Guided local customization offers a focused route to test demand and reduce adoption friction.

**Embedded and offline computing.** Equipment makers could use a controlled software foundation for devices that must remain useful without continuous cloud access. Applications could include industrial gateways, diagnostic equipment and field systems, with hardware validation and support for each deployment.

**Constrained AI automation.** Explicit permissions and checked program boundaries could help contain the authority granted to AI-generated software. The longer-term direction includes agents operating under compiled policy across devices. The compiler supplies a potential enforcement layer; reliable end-to-end agent operation remains a broader challenge.

**Software assurance.** Reproducible builds, independent checks and generated evidence could support customers who need to explain how software was built and which controls were applied. Such evidence can assist review and audit work; formal certification remains a separate process.

## The commercial proposition

Potential revenue paths include supported developer tools, enterprise integration, device support and assurance tooling. Customer pilots would test whether clearer controls, easier deployment and inspectable software create sufficient value. These are business models to validate, rather than established revenue claims.

The near-term investment case is a focused product built on a reusable platform. An initial product could establish demand and distribution; the compiler, runtime and verification work could support additional products without rebuilding the foundation for each market.

The next commercial milestones are a clearly selected customer segment, repeatable pilots, measured customer benefit, and support commitments that match the product's maturity. Investment could accelerate product packaging, target validation, independent security review and customer development.

*Project and demonstrations: [cobblestoneproject.com](https://cobblestoneproject.com). Technical basis: project source, TechnicalDetails.md, DevelopersGuide.md and ModBuilder verification records. Product possibilities are proposed directions, not customer-adoption claims.*
