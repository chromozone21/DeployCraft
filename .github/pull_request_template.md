## Quoi

<!-- Une ou deux phrases : ce que fait cette PR. -->

## Pourquoi

<!-- La demande ou le problème d'origine. -->

## Lignes de PARITE.md

<!-- Ex. 5.1, 5.2 — et elles sont mises à jour dans cette PR. -->

## Comment tester

<!-- Tests automatiques ajoutés ; et à la main : instance isolée (port 5091) ? Sur quel poste ? Captures si l'interface change. -->

## Vérifications

- [ ] `dotnet build DeployCraft.slnx` passe sans erreur ni avertissement
- [ ] `dotnet test DeployCraft.slnx` passe ; tests ajoutés ou mis à jour pour le comportement modifié
- [ ] `dotnet format DeployCraft.slnx --verify-no-changes` passe
- [ ] `PARITE.md` (et `SPEC.md` si besoin) mis à jour
- [ ] Réalisation indépendante : rien de copié d'un autre logiciel (code, icônes, textes)
- [ ] Aucun secret ni information interne (serveurs, postes, domaines, comptes) dans `git diff --staged`, le code ou les captures ; `.gitignore` intact
- [ ] Commits au format `feat:` / `fix:` / `docs:` …, un changement logique par commit
