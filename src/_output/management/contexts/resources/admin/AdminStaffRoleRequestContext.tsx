"use client"

import {
  ReactNode,
  createContext,
  useContext,
  useState,
} from "react"
import { Api } from "@/services/api.service"
import {
  AdminStaffRoleRequest,
  AdminStaffRoleRequestSearchResponse,
  AdminStaffRoleRequestUpdateRequest,
  AdminStaffRoleRequestSubmitRequest,
  AdminStaffRoleRequestValidateRequest,
} from "@/types/api.types"
import { FetchRequest, DEFAULT_PAGE_SIZE, ValidateResponse } from "@sseta/components"

interface AdminStaffRoleRequestContextType {
  // State
  items: AdminStaffRoleRequestSearchResponse[]
  totalRows: number
  lastSearchTerm: string
  lastFetchRequest: FetchRequest | null

  // Operations
  fetchItems: (fetchRequest: FetchRequest, shouldMerge?: boolean) => Promise<void>
  retrieve: (staffRoleRequestId: number) => Promise<AdminStaffRoleRequest | null>
  update: (data: AdminStaffRoleRequestUpdateRequest) => Promise<boolean>
  submit: (data: AdminStaffRoleRequestSubmitRequest) => Promise<boolean>
  validate: (data: AdminStaffRoleRequestValidateRequest) => Promise<ValidateResponse | null>
  loadMoreItems: () => Promise<void>

  // Refresh
  refresh: () => Promise<void>
}

// Undefined default is intentional — enforced by the useAdminStaffRoleRequest hook below.
const AdminStaffRoleRequestContext = createContext<AdminStaffRoleRequestContextType | undefined>(undefined)

export function AdminStaffRoleRequestProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<any>({
    items: [],
    totalRows: 0,
    lastSearchTerm: "",
    lastFetchRequest: {
      pageNumber: 1,
      pageSize: DEFAULT_PAGE_SIZE,
      orderByList: [],
      filterByList: [],
    },
  })

  const fetchItems = async (body: FetchRequest, shouldMerge: boolean = false) => {
    try {
      const response = await Api.ADMIN.StaffRoleRequest.search(body)
      const searchFilter = body.filterByList?.find((f) => f.operator === "search")
      const searchTerm = searchFilter ? (searchFilter.value as string).replace(/\*/g, "") : ""
      setState((prev: any) => ({
        ...prev,
        items: shouldMerge ? [...prev.items, ...(response.data.searchResults || [])] : response.data.searchResults || [],
        totalRows: response.data?.totalRows || 0,
        lastSearchTerm: searchTerm,
        lastFetchRequest: body,
      }))
    } catch (error) {
      console.error(error)
      throw error
    }
  }

  const retrieve = async (staffRoleRequestId: number): Promise<AdminStaffRoleRequest | null> => {
    try {
      const response = await Api.ADMIN.StaffRoleRequest.retrieve(staffRoleRequestId)
      return response.data
    } catch (error) {
      console.error(error)
      throw error
    }
  }

  const update = async (data: AdminStaffRoleRequestUpdateRequest): Promise<boolean> => {
    try {
      await Api.ADMIN.StaffRoleRequest.update(data)
      if (state.lastFetchRequest) await fetchItems(state.lastFetchRequest)
      return true
    } catch (error) {
      console.error(error)
      throw error
    }
  }

  const submit = async (data: AdminStaffRoleRequestSubmitRequest): Promise<boolean> => {
    try {
      const response = await Api.ADMIN.StaffRoleRequest.submit(data)
      if (state.lastFetchRequest) await fetchItems(state.lastFetchRequest)
      return response.data ?? false
    } catch (error) {
      console.error(error)
      throw error
    }
  }

  const validate = async (data: AdminStaffRoleRequestValidateRequest): Promise<ValidateResponse | null> => {
    try {
      const response = await Api.ADMIN.StaffRoleRequest.validate(data)
      return response.data
    } catch (error) {
      console.error(error)
      throw error
    }
  }

  const refresh = async () => { if (state.lastFetchRequest) await fetchItems(state.lastFetchRequest) }
  const loadMoreItems = async () => {
    if (state.lastFetchRequest) {
      await fetchItems({ ...state.lastFetchRequest, pageNumber: state.lastFetchRequest.pageNumber + 1 }, true)
    }
  }

  // No useMemo — the React Compiler handles memoization.
  return (
    <AdminStaffRoleRequestContext.Provider
      value={{
        ...state,
        fetchItems,
        retrieve,
        update,
        submit,
        validate,
        loadMoreItems,
        refresh,
      }}
    >
      {children}
    </AdminStaffRoleRequestContext.Provider>
  )
}

// Throws if used outside of AdminStaffRoleRequestProvider to catch missing provider wrapping early.
export function useAdminStaffRoleRequest() {
  const context = useContext(AdminStaffRoleRequestContext)
  if (context === undefined) {
    throw new Error("useAdminStaffRoleRequest must be used within a AdminStaffRoleRequestProvider")
  }
  return context
}
