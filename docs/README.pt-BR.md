# Sem ~~mais~~ bagunça

<img src="https://raw.githubusercontent.com/6a6f6a6f/assets/main/clone.logo.png" align="right"
     alt="clone logo by Jojo" width="120" height="120">

Eu tenho um problema muito sério de acumular uma quantidade absurda de *clones* no meu computador, e faço isso ao acaso, em diretórios aleatórios, sem nenhuma organização. Isso é um problema, pois eu não consigo encontrar nada, e acabo tendo que baixar tudo de novo. Um *fun fact* sobre isso, é que eu tenho um *storage* de 8TB — em *raid 10*, afinal de contas somos todos cultos por aqui —, e ele está quase cheio de tralhas OSS, e eu não sei o que tem nele.

Eu provavelmente deveria ter olhado a documentação do Git de forma mais profunda, eu realmente não sei se isso que implementei é uma *feature*, mas como diria o profeta: Qual o sentido de ver algo que você pode gastar uma horinha para entender, se você pode gastar 100 horas para implementar algo que você não entende? Enfim, aqui vai as *features* dessa simpática ferramenta chamada `clone`:

- **Nativo** (sim, [dotnet tem um AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)), e **sem nenhuma dependência**;
- Salva suas tralhas OSS de forma organizada pelo `owner`; e
- Você pode escolher o diretório que deseja salvar os projetos.

No final das contas vai ficar algo assim:

```plaintext
.
└── Projects
    ├── user1
    │   ├── projectA
    │   ├── projectB
    │   └── ...
    ├── user2
    │   ├── projectA
    │   ├── projectB
    │   └── ...
    └── ...
```

É bem simples, mas é de coração. Eu espero que você goste.

## Configuração

Literalmente existe apenas uma única configuração, que é o diretório onde você deseja salvar os seus projetos. Você pode fazer isso de um jeito bem simples, apenas criando uma variável de ambiente chamada `CLONE_PROJECT_FOLDER`, apontando para o diretório raiz onde você deseja salvar os seus projetos. Por exemplo, no meu caso, eu tenho um diretório chamado `Projectos` na raiz do meu "`$HOME`", então eu fiz o seguinte no meu `$PROFILE` do PowerShell:

```powershell
$env:CLONE_PROJECT_FOLDER="C:\Users\jojo\Projects"
```

No Linux e no macOS, você pode fazer o mesmo, mas com um `export`, no seu `.bashrc` ou `.zshrc` (ou seja lá o que você usa):

```bash
export CLONE_PROJECT_FOLDER="$HOME/Projects"
```

E é isso. Você pode usar o `clone` sem nenhuma configuração adicional.

## Instalação

Basta verificar o último *release* e baixar o binário correspondente ao seu sistema operacional. Dentro do `.zip` tera o binário do `clone`, e você pode colocar ele onde quiser, e adicionar no seu `$PATH`.

## Build

Deixei um `Makefile` para facilitar a sua vida. Você pode usar o `make` para compilar o `clone` chamando `make publish`, e consumir o artefato gerado no respectivo destino dado o seu SO.

## Licença

Como não tenho paciência para escolher uma licença, considere a WTFPL como a licença desse projeto. Você pode fazer o que quiser com o código, e eu não me importo com nada. A única coisa que eu peço é que você não me processe se algo der errado, ou se precisar de algum suporte.
