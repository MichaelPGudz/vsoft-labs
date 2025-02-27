from locust import HttpUser, task, tag, between
from random import choice, randint
import json

class TodoUser(HttpUser):
    # Symulacja realnych przerw między akcjami użytkownika
    wait_time = between(1, 3)
    
    def on_start(self):
        """Inicjalizacja - tworzymy kilka początkowych todos dla użytkownika"""
        self.todo_ids = []
        # Tworzymy 3 początkowe todos
        for i in range(3):
            todo = {
                "name": f"Initial Todo {i}",
                "isComplete": False
            }
            with self.client.post("/todoitems", json=todo, catch_response=True) as response:
                if response.status_code == 201:
                    todo_id = int(response.headers['Location'].split('/')[-1])
                    self.todo_ids.append(todo_id)

    @task(3)  # Większy weight dla odczytu - najczęstsza operacja
    @tag('read')
    def read_todos(self):
        """Symulacja przeglądania listy todos"""
        self.client.get("/todoitems")

    @task(2)
    @tag('read')
    def read_single_todo(self):
        """Symulacja sprawdzania pojedynczego todo"""
        if self.todo_ids:
            todo_id = choice(self.todo_ids)
            self.client.get(f"/todoitems/{todo_id}")

    @task(2)
    @tag('read')
    def read_complete_todos(self):
        """Sprawdzanie ukończonych zadań"""
        self.client.get("/todoitems/complete")

    @task(1)
    @tag('write')
    def create_todo(self):
        """Symulacja tworzenia nowego todo"""
        new_todo = {
            "name": f"Load Test Todo {randint(1000, 9999)}",
            "isComplete": False
        }
        with self.client.post("/todoitems", json=new_todo, catch_response=True) as response:
            if response.status_code == 201:
                todo_id = int(response.headers['Location'].split('/')[-1])
                self.todo_ids.append(todo_id)
                if len(self.todo_ids) > 10:  # Limit przechowywanych ID
                    self.todo_ids.pop(0)

    @task(1)
    @tag('write')
    def update_todo(self):
        """Symulacja aktualizacji istniejącego todo"""
        if self.todo_ids:
            todo_id = choice(self.todo_ids)
            updated_todo = {
                "id": todo_id,
                "name": f"Updated Todo {randint(1000, 9999)}",
                "isComplete": choice([True, False])
            }
            self.client.put(f"/todoitems/{todo_id}", json=updated_todo)

    @task(1)
    @tag('write')
    def delete_todo(self):
        """Symulacja usuwania todo"""
        if self.todo_ids:
            todo_id = choice(self.todo_ids)
            with self.client.delete(f"/todoitems/{todo_id}", catch_response=True) as response:
                if response.status_code == 204:
                    self.todo_ids.remove(todo_id)

class ReadOnlyUser(TodoUser):
    """Użytkownik który tylko czyta - symulacja użytkowników przeglądających"""
    @task(1)
    @tag('read')
    def read_todos(self):
        self.client.get("/todoitems")
    
    @task(1)
    @tag('read')
    def read_complete_todos(self):
        self.client.get("/todoitems/complete")
