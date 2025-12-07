# КПО Антиплагиат. ДЗ-3

Выполнил: Малапура Артемий Андреевич
Группа: БПИ-247

## Архитектура

Серверная часть состоит из трёх микросервисов и базы данных postgresql:

1. **API Gateway (Gateway)**
   - Принимает запросы от клиентов
   - При загрузке работы:
     - отправляет файл в FileStorageService
     - читает содержимое файла
     - сохраняет запись о работе в postgresql
     - вызывает FileAnalysisService для анализа
     - сохраняет отчёт в postgresql
     - возвращает клиенту итоговый JSON, где написано плагиат или нет
   - Позволяет получить отчёты по конкретному заданию
   - Позволяет получить ссылку на облако слов (word cloud) по конкретной работе.

2. **FileStorageService**
   - Отвечает только за хранение и выдачу файлов
   - Принимает файл, сохраняет его в папку /files внутри контейнера
   - Возвращает сгенерированное имя файла

3. **FileAnalysisService**
   - Отвечает только за анализ текста и определение признаков плагиата
   - Читает данные о работе и других работах по тому же заданию из postgresql
   - Считает схожесть на основе текста и возвращает отчёт в API Gateway

4. **postgresql**
   - Хранит таблицу submissions с работами и таблицу reports с результатами анализа

**API Gateway** маршрутизирует запросы к остальным сервисам и предоставляет единый публичный API

## Модель данных

Таблицы создаются автоматически при старте контейнера postgresql по скрипту database/init.sql

```sql
CREATE TABLE IF NOT EXISTS submissions (
    id SERIAL PRIMARY KEY,
    student_name TEXT NOT NULL,
    assignment_name TEXT NOT NULL,
    file_name TEXT NOT NULL,
    content TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS reports (
    id SERIAL PRIMARY KEY,
    submission_id INT NOT NULL REFERENCES submissions(id) ON DELETE CASCADE,
    is_plagiarism BOOLEAN NOT NULL,
    similarity DOUBLE PRECISION NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    details TEXT
);
```

- submissions — все присланные работы студентов.
- reports — результат анализа каждой работы (флаг плагиата, коэффициент схожести и т.д.).

## Определение плагиата

Алгоритм в FileAnalysisService/Utils/TextUtils.cs

1. Для текста текущей работы и всех других работ **по тому же заданию**:
   - текст разбивается на токены — последовательности букв/цифр (разделители: пробелы, знаки препинания и т.п.)
   - все токены приводятся к нижнему регистру
   - строятся множества уникальных слов A и B

2. Для каждой пары «текущая работа — другая работа» считается **коэффициент Жаккара** (https://sky.pro/wiki/analytics/rasstoyanie-zhakkara-principy-primenenie-i-osobennosti-metoda/):

   \[J(A, B) = \frac{|A \cap B|}{|A \cup B|}\]

3. Выбирается максимальное значение MaxSimilarity и ближайшая по схожести работа ClosestSubmissionId

4. Работа считается плагиатом, если:

   ```text
   MaxSimilarity >= 0.8 (80%)
   ```

5. Результат (IsPlagiarism, MaxSimilarity, ClosestSubmissionId) возвращается в API Gateway, который записывает его в таблицу reports

---

### Сборка и запуск

```bash
docker compose build
docker compose up
```

- Swagger: <http://localhost:8080/swagger>

---

## Тестирование API

### Тестовые файлы (Все три файла относятся к заданию HW1)

samples/work1_ivanov.txt:

```text
This is the solution for assignment 1 about sorting algorithms.
We implement quick sort, merge sort and insertion sort.
The program reads numbers from input and prints them sorted.
The explanation describes time complexity O(n log n).
```

samples/work2_petrov.txt:

```text
In this work I describe my favorite football team and the World Cup.
There is no information about algorithms or programming here.
The text is completely unrelated to sorting or computer science.
```

samples/work3_sidorov_plag.txt (почти копия первого):

```text
This is the solution for assignment 1 about sorting algorithms.
We implement quick sort, merge sort and insertion sort.
The program reads numbers from input and prints them sorted.
The explanation describes time complexity O(n log n).
This text is almost the same as another student's work.
```

---

### Загрузка работ (POST /api/submissions)

#### 1 - Иванов

```bash
curl -X POST "http://localhost:8080/api/submissions" \
  -F "studentName=Иванов Иван" \
  -F "assignmentName=HW1" \
  -F "file=@samples/work1_ivanov.txt"
```

Ответ:

```json
{"submissionId":1,"reportId":1,"studentName":"Иванов Иван","assignmentName":"HW1","fileName":"1931aaa3-86c5-4039-8d8e-db035f131f46_work1_ivanov.txt","isPlagiarism":false,"similarity":0}
```

#### 2 - Петров

```bash
curl -X POST "http://localhost:8080/api/submissions" \
  -F "studentName=Петров Петр" \
  -F "assignmentName=HW1" \
  -F "file=@samples/work2_petrov.txt"
```

Ответ:

```json
{"submissionId":2,"reportId":2,"studentName":"Петров Петр","assignmentName":"HW1","fileName":"b5bd7417-6a0d-4dca-8c14-d631a4aeb4b3_work2_petrov.txt","isPlagiarism":false,"similarity":0.12962962962962962}
```

#### 3 - Сидоров (плагиат)

```bash
curl -X POST "http://localhost:8080/api/submissions" \
  -F "studentName=Сидоров Сидор" \
  -F "assignmentName=HW1" \
  -F "file=@samples/work3_sidorov_plag.txt"
```

Ответ:

```json
{"submissionId":3,"reportId":3,"studentName":"Сидоров Сидор","assignmentName":"HW1","fileName":"6fc08c47-d00e-41b3-a8b6-3774e6a25107_work3_sidorov_plag.txt","isPlagiarism":true,"similarity":0.8}
```

---

### Получение отчётов по заданию

Эндпоинт: GET /api/assignments/{assignmentName}/reports

Для задания HW1:

```bash
curl "http://localhost:8080/api/assignments/HW1/reports" | jq
```

Ответ:

```json
[
  {
    "submissionId": 1,
    "studentName": "Иванов Иван",
    "assignmentName": "HW1",
    "isPlagiarism": false,
    "similarity": 0,
    "createdAt": "2025-11-30T23:20:09.325805Z"
  },
  {
    "submissionId": 2,
    "studentName": "Петров Петр",
    "assignmentName": "HW1",
    "isPlagiarism": false,
    "similarity": 0.12962962962962962,
    "createdAt": "2025-11-30T23:20:54.299102Z"
  },
  {
    "submissionId": 3,
    "studentName": "Сидоров Сидор",
    "assignmentName": "HW1",
    "isPlagiarism": true,
    "similarity": 0.8,
    "createdAt": "2025-11-30T23:21:47.343517Z"
  }
]
```

---

### Облако слов (GET /api/submissions/{id}/wordcloud)

Эндпоинт: `GET /api/submissions/{id}/wordcloud`

Например, для работы Сидорова с `submissionId = 3`:

```bash
curl "http://localhost:8080/api/submissions/3/wordcloud"
```

Ответ:

```json
{"submissionId":3,"url":"https://quickchart.io/wordcloud?text=This%20is%20the%20solution%20for%20assignment%201%20about%20sorting%20algorithms.%0AWe%20implement%20quick%20sort%2C%20merge%20sort%20and%20insertion%20sort.%0AThe%20program%20reads%20numbers%20from%20input%20and%20prints%20them%20sorted.%0AThe%20explanation%20describes%20time%20complexity%20O%28n%20log%20n%29.%0AThis%20text%20is%20almost%20the%20same%20as%20another%20student%27s%20work."}
```