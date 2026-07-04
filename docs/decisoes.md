# Decisões de arquitetura (por quê)

- **Identidade própria (`app_user`), não o Firebase uid como PK.** Evita amarração ao provedor; trocar de auth mexe em uma coluna (`firebase_uid`), não no schema todo. `owner_id` é sempre o id interno.
- **Client-managed auth + JIT provisioning.** Front cria conta/loga no Firebase (SDK); senha nunca passa pela API; backend só valida JWT e provisiona `app_user` no 1º acesso. Sem Admin SDK.
- **Molde × lançamento separados, com snapshot.** `planned_amount`/`split_ratio_snapshot` copiados no `*_entry`, nunca FK. Base da imutabilidade: editar molde não reescreve o passado.
- **Imutabilidade por congelamento de pagos**, não por tabela de auditoria. Pago/recebido trava; unpay/unreceive descongela. Recálculo respeita o congelamento.
- **Sem entidades "a pagar/paga".** Estado é atributo (`paid`/`received`) do mesmo registro; duas tabelas criariam sincronização sem ganho.
- **A-receber dentro do `bill_entry`.** `person_id`+`received`+`received_date`; sem entidade de dívida. `paid` ≠ `received`.
- **Recebimento em lote, sem troco.** Valores exatos na prática; marcação em lote na UI, modelo continua conta-a-conta. Entidade de settlement descartada.
- **Orçamento anual, projeção idempotente.** UNIQUE(molde,ano,mês)+ON CONFLICT DO NOTHING; rerodar não duplica nem toca pagos. Virada de ano = projeção de ano+1 (default_amount já reflete reajustes).
- **Split como fração numérica** (não booleano). Cobre 1.0/0.5/0.0 e futuros sem refatorar.
- **Soft delete nos moldes.** Preserva integridade dos lançamentos históricos que os referenciam.
- **Isolamento na aplicação** (filtro por owner_id), não RLS — mais simples com EF Core. RLS fica para eventual multi-tenant sério.
- **Azure Web App F1 + Neon (pooler).** Free; ciente do cold start (migrar depois se incomodar). Neon pooler + SSL no EF Core.
- **Migrations aplicadas pela pipeline, não no startup.** `dotnet ef database update` roda no job `deploy` do CI/CD (usa a `AppDbContextFactory` já existente para tooling), não mais em `Program.cs`. Evita reaplicar a checagem de migration a cada cold start do Azure Web App F1 e separa responsabilidade de deploy da de runtime.
- **Todos os endpoints sob `/api/v1`.** Antes havia mistura: CRUDs na raiz (`/bills`, `/categories`...) e o resto sob `/api/...`, sem versionamento. Padronizado com um único `var v1 = app.MapGroup("/api/v1")`, sem extrair para extension methods por recurso (ganho baixo frente ao tamanho do `Program.cs` atual; fica como possível follow-up). **Cutover direto, sem aliases das rotas antigas**: como é uma aplicação de uso pessoal com deploy único (Azure Web App + Firebase Hosting controlados pelo mesmo owner), o custo de manter um grupo de rotas legado em paralelo (roteamento duplicado, risco de divergência) superou o benefício; a coordenação foi feita casando o merge deste PR com o da migração do frontend (`bills-frontend#43`) para o mesmo prefixo.
