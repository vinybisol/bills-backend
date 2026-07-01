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
